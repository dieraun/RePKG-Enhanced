// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 控制台输出：进度行节流（避免每 16 MiB 刷一行把终端拖死），结果与汇总分行输出。
// 2.0.0：文案全部走本地化表，中英各一套。

using System;
using System.Globalization;
using System.IO;
using RePKG.Shared.Export;
using RePKG.Shared.Localization;

namespace RePKG.Cli
{
    public sealed class ConsoleReporter
    {
        private readonly TextWriter _out;
        private readonly ILocalizer _localizer;
        private readonly bool _interactive;
        private readonly object _gate = new object();

        private DateTime _lastProgress = DateTime.MinValue;
        private bool _lineOpen;

        public ConsoleReporter(TextWriter output, bool interactive, ILocalizer localizer)
        {
            _out = output ?? throw new ArgumentNullException(nameof(output));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
            _interactive = interactive;
        }

        public void Progress(ExportProgress progress)
        {
            if (!_interactive) return;

            var now = DateTime.UtcNow;

            if (now - _lastProgress < TimeSpan.FromMilliseconds(120)) return;

            _lastProgress = now;

            lock (_gate)
            {
                var percent = progress.EntriesTotal == 0
                    ? 0
                    : progress.EntriesDone * 100.0 / progress.EntriesTotal;

                _out.Write('\r');
                _out.Write("  ");
                _out.Write(_localizer.Format(
                    "cli.report.progress",
                    progress.PackagesDone + 1,
                    Math.Max(progress.PackagesTotal, 1),
                    progress.EntriesDone,
                    progress.EntriesTotal,
                    percent));
                _out.Write(' ');
                _out.Write(Truncate(progress.CurrentEntry ?? string.Empty, 48));
                _out.Write("                    ");
                _lineOpen = true;
            }
        }

        public void JobCompleted(ExportJobResult result)
        {
            lock (_gate)
            {
                ClearLine();

                var name = Path.GetFileName(result.PackagePath);
                var status = _localizer.Get(result.FatalError == null ? "cli.report.done" : "cli.report.failed");
                var duration = result.Duration.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + "s";

                _out.WriteLine(
                    $"[{status}] {name} → {result.OutputDirectory}  " +
                    _localizer.Format(
                        "cli.report.counts",
                        result.Succeeded,
                        result.Skipped,
                        result.Failed,
                        result.Canceled,
                        FormatBytes(result.BytesWritten),
                        duration));

                if (result.FatalError != null)
                    _out.WriteLine(_localizer.Format("cli.report.reason", result.FatalError));
            }
        }

        public void Summary(ExportSummary summary)
        {
            lock (_gate)
            {
                ClearLine();

                var duration = summary.Duration.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + "s";

                _out.WriteLine();
                _out.WriteLine(_localizer.Format(
                    "cli.report.summary",
                    summary.Packages,
                    summary.Succeeded,
                    summary.Skipped,
                    summary.Failed,
                    summary.Canceled,
                    FormatBytes(summary.BytesWritten),
                    duration));
            }
        }

        public void Info(string message)
        {
            lock (_gate)
            {
                ClearLine();
                _out.WriteLine(message);
            }
        }

        public void Error(string message)
        {
            lock (_gate)
            {
                ClearLine();
                _out.WriteLine(message);
            }
        }

        private void ClearLine()
        {
            if (!_lineOpen) return;

            _out.Write('\r');
            _out.Write(new string(' ', 100));
            _out.Write('\r');
            _lineOpen = false;
        }

        private static string Truncate(string value, int max)
            => value.Length <= max ? value : "..." + value.Substring(value.Length - (max - 3));

        public static string FormatBytes(long bytes)
        {
            string[] units = {"B", "KiB", "MiB", "GiB", "TiB"};
            double value = bytes;
            var unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0 ? $"{bytes} B" : $"{value:0.0} {units[unit]}";
        }
    }
}
