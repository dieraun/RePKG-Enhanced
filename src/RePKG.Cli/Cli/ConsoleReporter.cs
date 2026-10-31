// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 控制台输出：进度行节流（避免每 16 MiB 刷一行把终端拖死），结果与汇总分行输出。

using System;
using System.IO;
using RePKG.Shared.Export;

namespace RePKG.Cli
{
    public sealed class ConsoleReporter
    {
        private readonly TextWriter _out;
        private readonly bool _interactive;
        private readonly object _gate = new object();

        private DateTime _lastProgress = DateTime.MinValue;
        private bool _lineOpen;

        public ConsoleReporter(TextWriter output, bool interactive)
        {
            _out = output ?? throw new ArgumentNullException(nameof(output));
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
                _out.Write($"  [{progress.PackagesDone + 1}/{Math.Max(progress.PackagesTotal, 1)}] ");
                _out.Write($"{progress.EntriesDone}/{progress.EntriesTotal} ({percent:0.0}%) ");
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
                var status = result.FatalError == null ? "完成" : "失败";

                _out.WriteLine(
                    $"[{status}] {name} → {result.OutputDirectory}  " +
                    $"成功 {result.Succeeded} / 跳过 {result.Skipped} / 失败 {result.Failed} / 取消 {result.Canceled}，" +
                    $"{FormatBytes(result.BytesWritten)}，{result.Duration.TotalSeconds:0.0}s");

                if (result.FatalError != null)
                    _out.WriteLine($"       原因：{result.FatalError}");
            }
        }

        public void Summary(ExportSummary summary)
        {
            lock (_gate)
            {
                ClearLine();

                _out.WriteLine();
                _out.WriteLine(
                    $"汇总：{summary.Packages} 个包，成功 {summary.Succeeded} / 跳过 {summary.Skipped} / " +
                    $"失败 {summary.Failed} / 取消 {summary.Canceled}，共 {FormatBytes(summary.BytesWritten)}，" +
                    $"{summary.Duration.TotalSeconds:0.0}s");
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
