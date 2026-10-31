using System;

namespace RePKG.Shared.Export
{
    /// <summary>单个条目的导出状态。</summary>
    public enum ExportStatus
    {
        /// <summary>排队中，尚未开始。</summary>
        Pending,
        /// <summary>正在写盘。</summary>
        Running,
        /// <summary>已成功写出（含"已存在且未要求覆盖"的跳过）。</summary>
        Succeeded,
        /// <summary>跳过（过滤、已存在、路径安全检查拒绝）。</summary>
        Skipped,
        /// <summary>该条目失败，但批量继续。</summary>
        Failed,
        /// <summary>因取消而未处理。</summary>
        Canceled
    }

    /// <summary>条目的导出结果。<see cref="Error"/> 为空表示成功或跳过。</summary>
    public sealed class ExportEntryResult
    {
        public ExportEntryResult(string fullPath, string? targetPath, long length, ExportStatus status, string? error)
        {
            FullPath = fullPath;
            TargetPath = targetPath;
            Length = length;
            Status = status;
            Error = error;
        }

        /// <summary>pkg 内的相对路径。</summary>
        public string FullPath { get; }

        /// <summary>输出文件绝对路径（被拒绝/跳过时可能为 null）。</summary>
        public string? TargetPath { get; }

        /// <summary>条目原始字节数。</summary>
        public long Length { get; }

        public ExportStatus Status { get; }

        /// <summary>失败原因；成功为 null。</summary>
        public string? Error { get; }

        public bool IsFailure => Status == ExportStatus.Failed;
    }

    /// <summary>单个 pkg 的导出汇总。</summary>
    public sealed class ExportJobResult
    {
        public ExportJobResult(
            string packagePath,
            string outputDirectory,
            int succeeded,
            int skipped,
            int failed,
            int canceled,
            long bytesWritten,
            TimeSpan duration,
            string? fatalError,
            string? logPath)
        {
            PackagePath = packagePath;
            OutputDirectory = outputDirectory;
            Succeeded = succeeded;
            Skipped = skipped;
            Failed = failed;
            Canceled = canceled;
            BytesWritten = bytesWritten;
            Duration = duration;
            FatalError = fatalError;
            LogPath = logPath;
        }

        public string PackagePath { get; }
        public string OutputDirectory { get; }
        public int Succeeded { get; }
        public int Skipped { get; }
        public int Failed { get; }
        public int Canceled { get; }
        public long BytesWritten { get; }
        public TimeSpan Duration { get; }

        /// <summary>整包级别的失败（打不开、格式错），非 null 时其余计数无意义。</summary>
        public string? FatalError { get; }

        /// <summary>该包的 JSONL 日志路径（启用日志时）。</summary>
        public string? LogPath { get; }

        public bool IsSuccess => FatalError == null && Failed == 0;
    }

    /// <summary>整体导出的汇总（多次 <see cref="ExportJobResult"/> 之和）。</summary>
    public sealed class ExportSummary
    {
        public ExportSummary(int packages, int succeeded, int skipped, int failed, int canceled, long bytesWritten, TimeSpan duration)
        {
            Packages = packages;
            Succeeded = succeeded;
            Skipped = skipped;
            Failed = failed;
            Canceled = canceled;
            BytesWritten = bytesWritten;
            Duration = duration;
        }

        public int Packages { get; }
        public int Succeeded { get; }
        public int Skipped { get; }
        public int Failed { get; }
        public int Canceled { get; }
        public long BytesWritten { get; }
        public TimeSpan Duration { get; }

        /// <summary>有失败条目或整包失败时为 false（CLI 据此返回非零退出码）。</summary>
        public bool IsSuccess => Failed == 0;
    }
}
