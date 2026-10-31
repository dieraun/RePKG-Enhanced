// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// extract 子命令：解析输入 → 构造 ExportOptions → 全部交给同一个 ExportQueue。
// 入口层在这里只做"发现输入 + 打印结果"，条目循环一行都没有。

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RePKG.Application.Package;
using RePKG.Shared.Export;
using RePKG.Shared.Logging;
using RePKG.Shared.Paths;

namespace RePKG.Cli
{
    internal static class ExtractCommand
    {
        public static async Task<ExitCode> RunAsync(
            CliOptions options,
            TextWriter output,
            CancellationToken cancellationToken)
        {
            var reporter = new ConsoleReporter(output, interactive: !Console.IsOutputRedirected);
            var packages = InputResolver.CollectPackages(options.Inputs, options.Recursive);
            var outputRoot = Path.GetFullPath(options.Output ?? "output");

            var exportOptions = new ExportOptions
            {
                OutputDirectory = outputRoot,
                OnlyExtensions = options.OnlyExtensions,
                IgnoreExtensions = options.IgnoreExtensions,
                SingleDirectory = options.SingleDirectory,
                Overwrite = options.Overwrite,
                Concurrency = options.Concurrency ?? 1,
                QueueCapacity = options.QueueCapacity ?? 64,
                WriteLog = !options.NoLog,
                WriteManifest = options.Manifest
            };

            var converter = options.NoTexConvert ? null : new TexEntryConverter();

            await using (var queue = new ExportQueue(exportOptions, NullExportLog.Instance, converter))
            {
                queue.ProgressChanged += reporter.Progress;
                queue.JobCompleted += reporter.JobCompleted;

                using (cancellationToken.Register(state => ((ExportQueue) state!).Control.Cancel(), queue))
                {
                    reporter.Info($"共 {packages.Count} 个 pkg → {outputRoot}" +
                                  (converter == null ? "（不转换 TEX）" : "（TEX 转 PNG/GIF/MP4）"));

                    var jobs = new List<Task<ExportJobResult>>(packages.Count);

                    foreach (var package in packages)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        string packageDirectory;

                        try
                        {
                            packageDirectory = ResolvePackageDirectory(package, outputRoot, options);

                            if (options.CopyProject || options.UseName)
                                PreparePackageDirectory(package, packageDirectory, options);
                        }
                        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                        {
                            reporter.Error($"[跳过] {Path.GetFileName(package)}：{ex.Message}");
                            continue;
                        }

                        jobs.Add(queue.EnqueueAsync(package, packageDirectory));
                    }

                    foreach (var job in jobs)
                        await job.ConfigureAwait(false);

                    var summary = await queue.CompleteAsync().ConfigureAwait(false);

                    reporter.Summary(summary);

                    if (summary.Canceled > 0)
                        return ExitCode.Canceled;

                    return summary.IsSuccess ? ExitCode.Success : ExitCode.PartialFailure;
                }
            }
        }

        /// <summary>
        /// 每个 pkg 输出到独立子目录，避免批量提取同名条目互相覆盖。
        /// -n 时用 project.json 的 title（读不到则退回文件名）。
        /// </summary>
        private static string ResolvePackageDirectory(string packagePath, string outputRoot, CliOptions options)
        {
            var name = Path.GetFileNameWithoutExtension(packagePath);

            if (options.UseName)
            {
                using (var source = PackageFileSource.Open(packagePath))
                {
                    var title = ProjectJson.TryReadTitle(source);

                    if (!string.IsNullOrWhiteSpace(title))
                        name = title!;
                }
            }

            if (!SafePath.TryResolve(outputRoot, name, out var resolved, out _))
                resolved = Path.Combine(outputRoot, FileNameSanitizer.Flatten(name));

            return resolved;
        }

        private static void PreparePackageDirectory(string packagePath, string packageDirectory, CliOptions options)
        {
            Directory.CreateDirectory(packageDirectory);

            if (!options.CopyProject)
                return;

            using (var source = PackageFileSource.Open(packagePath))
                ProjectJson.TryCopy(source, packageDirectory, options.Overwrite);
        }
    }
}
