// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// info 子命令：只读条目表（不碰数据区），列出条目清单；--json 给脚本用。
// 2.0.0：值说明文案走本地化表（键名 pkg/size/head/items/total 保持语言中立）。

using System;
using System.IO;
using RePKG.Application.Package;
using RePKG.Shared.Catalog;
using RePKG.Shared.Localization;

namespace RePKG.Cli
{
    internal static class InfoCommand
    {
        public static ExitCode Run(CliOptions options, TextWriter output, ILocalizer localizer)
        {
            var packages = InputResolver.CollectPackages(options.Inputs, options.Recursive, localizer);
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
                            WriteText(output, package, source, localizer);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                {
                    output.WriteLine(localizer.Format("cli.info.failed", package, ex.Message));
                    failed = true;
                }
            }

            return failed ? ExitCode.PartialFailure : ExitCode.Success;
        }

        private static void WriteText(TextWriter output, string packagePath, PackageFileSource source, ILocalizer localizer)
        {
            output.WriteLine($"pkg   : {packagePath}");
            output.WriteLine($"size  : {localizer.Format("cli.info.size", ConsoleReporter.FormatBytes(source.FileLength), source.FileLength)}");
            output.WriteLine($"head  : {localizer.Format("cli.info.header", source.HeaderSize, source.DataStart)}");
            output.WriteLine($"items : {source.Entries.Count}");

            long total = 0;

            foreach (var entry in source.Entries)
            {
                total += entry.Length;
                output.WriteLine($"  {entry.Length,14}  {entry.FullPath}");
            }

            output.WriteLine($"total : {localizer.Format("cli.info.bytes", total)}");
            output.WriteLine();
        }

        private static void WriteJson(TextWriter output, string packagePath, PackageFileSource source)
        {
            // 与 `extract --manifest` 共用 PackageManifest，避免两种 JSON 格式漂移
            output.WriteLine(PackageManifest.ToJson(source));
        }
    }
}
