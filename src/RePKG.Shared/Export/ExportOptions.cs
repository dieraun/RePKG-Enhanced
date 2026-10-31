using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RePKG.Shared.Export
{
    /// <summary>
    /// 导出参数的唯一来源。CLI 解析命令行、GUI 读取界面，最终都构造同一个对象，
    /// 交给同一个 <see cref="ExportQueue"/>。
    /// </summary>
    public sealed class ExportOptions
    {
        /// <summary>输出根目录（相对路径按当前工作目录解析）。</summary>
        public string OutputDirectory { get; set; } = "output";

        /// <summary>只导出这些扩展名（含点，大小写不敏感）；为空表示不限制。</summary>
        public IReadOnlyCollection<string> OnlyExtensions { get; set; } = Array.Empty<string>();

        /// <summary>排除这些扩展名；优先级高于 <see cref="OnlyExtensions"/>（与原版语义一致）。</summary>
        public IReadOnlyCollection<string> IgnoreExtensions { get; set; } = Array.Empty<string>();

        /// <summary>所有条目直接放进输出目录，不保留 pkg 内的子目录结构。</summary>
        public bool SingleDirectory { get; set; }

        /// <summary>目标文件已存在时覆盖；为 false 时记为 Skipped。</summary>
        public bool Overwrite { get; set; }

        /// <summary>同时处理的 pkg 数量。默认 1：任一时刻只保留一个流式读句柄。</summary>
        public int Concurrency { get; set; } = 1;

        /// <summary>排队中的 pkg 数量上限，超过则拒绝入队（背压，防止批量本身变成 OOM 源）。</summary>
        public int QueueCapacity { get; set; } = 64;

        /// <summary>每个 pkg 是否写一份 JSONL 日志到输出目录。</summary>
        public bool WriteLog { get; set; } = true;

        /// <summary>日志文件名（写在各自输出目录下）。</summary>
        public string LogFileName { get; set; } = "export-log.jsonl";

        /// <summary>复制缓冲区大小；必须 ≥ <c>Constants.MinimumCopyBufferSize</c>。</summary>
        public int CopyBufferSize { get; set; } = 1 << 20;

        /// <summary>取消后是否删除已完成条目（默认 false：保留已完成，只清理未完成的临时文件）。</summary>
        public bool RollbackOnCancel { get; set; }

        /// <summary>转换 .tex 时是否同时写出同名 .json 元信息（原版默认写，保持兼容）。</summary>
        public bool WriteTexInfo { get; set; } = true;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(OutputDirectory))
                throw new ArgumentException("输出目录为空", nameof(OutputDirectory));

            if (Concurrency < 1)
                throw new ArgumentOutOfRangeException(nameof(Concurrency), Concurrency, "并发数必须 ≥ 1");

            if (QueueCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(QueueCapacity), QueueCapacity, "队列上限必须 ≥ 1");

            if (CopyBufferSize < RePKG.Application.Constants.MinimumCopyBufferSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(CopyBufferSize),
                    CopyBufferSize,
                    $"缓冲区不得小于 {RePKG.Application.Constants.MinimumCopyBufferSize} 字节");
            }
        }
    }

    /// <summary>导出进度快照。GUI 绑定它，CLI 打印它。</summary>
    public sealed class ExportProgress
    {
        public ExportProgress(
            string packagePath,
            string? currentEntry,
            long entryBytesDone,
            long entryBytesTotal,
            int entriesDone,
            int entriesTotal,
            int packagesDone,
            int packagesTotal,
            long bytesWritten)
        {
            PackagePath = packagePath;
            CurrentEntry = currentEntry;
            EntryBytesDone = entryBytesDone;
            EntryBytesTotal = entryBytesTotal;
            EntriesDone = entriesDone;
            EntriesTotal = entriesTotal;
            PackagesDone = packagesDone;
            PackagesTotal = packagesTotal;
            BytesWritten = bytesWritten;
        }

        public string PackagePath { get; }
        public string? CurrentEntry { get; }
        public long EntryBytesDone { get; }
        public long EntryBytesTotal { get; }
        public int EntriesDone { get; }
        public int EntriesTotal { get; }
        public int PackagesDone { get; }
        public int PackagesTotal { get; }
        public long BytesWritten { get; }

        public double EntryFraction => EntryBytesTotal <= 0 ? 0 : (double) EntryBytesDone / EntryBytesTotal;
    }

    /// <summary>队列状态。</summary>
    public enum ExportQueueState
    {
        Idle,
        Running,
        Paused,
        Canceling,
        Completed
    }

    /// <summary>暂停/恢复/取消的唯一入口；也用于在条目边界响应取消。</summary>
    public sealed class ExportControl
    {
        private readonly SemaphoreSlim _resumeGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private volatile bool _paused;

        public ExportControl()
        {
            _resumeGate.Wait();
            if (!_paused) _resumeGate.Release();
        }

        public CancellationToken Token => _cancellation.Token;

        public bool IsPaused => _paused;

        public bool IsCancellationRequested => _cancellation.IsCancellationRequested;

        public void Pause()
        {
            if (_paused) return;
            _paused = true;
            _resumeGate.Wait();
        }

        public void Resume()
        {
            if (!_paused) return;
            _paused = false;
            _resumeGate.Release();
        }

        public void Cancel() => _cancellation.Cancel();

        /// <summary>在条目边界调用：暂停时阻塞，取消时抛 <see cref="OperationCanceledException"/>。</summary>
        public async Task WaitIfPausedAsync()
        {
            Token.ThrowIfCancellationRequested();

            if (!_paused) return;

            await _resumeGate.WaitAsync(Token).ConfigureAwait(false);
            _resumeGate.Release();
        }

        public void Dispose()
        {
            _cancellation.Dispose();
            _resumeGate.Dispose();
        }
    }
}
