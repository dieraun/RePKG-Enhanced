// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// tex 子命令：把独立的 .tex 文件转成 png/gif/mp4（复用同一个 TexEntryConverter，
// 不在这里重新实现解码）。2.0.0：文案走本地化表。

using System;
using System.IO;
using System.Threading;
using RePKG.Shared.Export;
using RePKG.Shared.Localization;

namespace RePKG.Cli
{
    internal static class TexCommand
    {
        public static ExitCode Run(CliOptions options, TextWriter output, CancellationToken cancellationToken, ILocalizer localizer)
        {
            var files = InputResolver.CollectTexFiles(options.Inputs, options.Recursive, localizer);
            var outputRoot = Path.GetFullPath(options.Output ?? "output");

            Directory.CreateDirectory(outputRoot);

            var exportOptions = new ExportOptions { Overwrite = options.Overwrite, WriteTexInfo = true };
            var converter = new TexEntryConverter();

            var succeeded = 0;
            var failed = 0;
            var skipped = 0;

            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested)
                    return ExitCode.Canceled;

                try
                {
                    var target = Path.Combine(outputRoot, Path.GetFileName(file));
                    var result = converter.ConvertFile(file, target, exportOptions, cancellationToken);

                    if (result.Skipped)
                    {
                        skipped++;
                        output.WriteLine(localizer.Format("cli.tex.skipped", Path.GetFileName(file)));
                    }
                    else
                    {
                        succeeded++;
                        output.WriteLine(localizer.Format(
                            "cli.tex.done",
                            Path.GetFileName(file),
                            Path.GetFileName(result.PrimaryPath),
                            ConsoleReporter.FormatBytes(result.BytesWritten)));
                    }
                }
                catch (OperationCanceledException)
                {
                    return ExitCode.Canceled;
                }
                catch (Exception ex)
                {
                    failed++;
                    output.WriteLine(localizer.Format("cli.tex.failed", Path.GetFileName(file), ex.Message));
                }
            }

            output.WriteLine(localizer.Format("cli.texSummary", succeeded, skipped, failed));

            return failed > 0 ? ExitCode.PartialFailure : ExitCode.Success;
        }
    }
}
