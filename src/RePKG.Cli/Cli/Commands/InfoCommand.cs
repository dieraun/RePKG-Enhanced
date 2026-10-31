// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// info 子命令：只读条目表（不碰数据区），列出条目清单；--json 给脚本用。

using System;
using System.IO;
using RePKG.Application.Package;
using RePKG.Shared.Catalog;

namespace RePKG.Cli
{
    internal static class InfoCommand
    {
        public static ExitCode Run(CliOptions options, TextWriter output)
        {
            var packages = InputResolver.CollectPackages(options.Inputs, options.Recursive);
            var failed = false;

            foreach (var package in packages)
            {
                try
                {
                    using (var source = PackageFileSource.Open(package, validateEntryRanges: false))
                    {
                        if (options.Json)
                            WriteJson(output, package, source);
                        else
                            WriteText(output, package, source);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                {
                    output.WriteLine($"[失败] {package}：{ex.Message}");
                    failed = true;
                }
            }

            return failed ? ExitCode.PartialFailure : ExitCode.Success;
        }

        private static void WriteText(TextWriter output, string packagePath, PackageFileSource source)
        {
            output.WriteLine($"pkg   : {packagePath}");
            output.WriteLine($"size  : {ConsoleReporter.FormatBytes(source.FileLength)} ({source.FileLength} 字节)");
            output.WriteLine($"head  : {source.HeaderSize} 字节，数据区起点 {source.DataStart}");
            output.WriteLine($"items : {source.Entries.Count}");

            long total = 0;

            foreach (var entry in source.Entries)
            {
                total += entry.Length;
                output.WriteLine($"  {entry.Length,14}  {entry.FullPath}");
            }

            output.WriteLine($"total : {total} 字节");
            output.WriteLine();
        }

        private static void WriteJson(TextWriter output, string packagePath, PackageFileSource source)
        {
            // 与 `extract --manifest` 共用 PackageManifest，避免两种 JSON 格式漂移
            output.WriteLine(PackageManifest.ToJson(source));
        }
    }
}
