using System;
using System.Buffers;
using System.IO;
using System.Threading;
using RePKG.Core.Package;

namespace RePKG.Application.Package
{
    /// <summary>
    /// 条目数据的流式提取器 —— 全项目**唯一**的分块复制实现。
    ///
    /// 契约：
    ///  - 峰值内存为 <c>bufferSize</c>（默认 1 MiB，从 ArrayPool 租用），
    ///    与条目大小、包总体积、包数量都无关。
    ///  - 严格读满声明的 <paramref name="bufferSize"/> 语义之外不越界：只复制 entry.Length 个字节，
    ///    源流提前结束即抛 <see cref="EndOfStreamException"/>（原版 <c>ReadBytes</c> 会静默返回短数组）。
    ///  - 任何时刻只持有源流上的一个顺序读游标；配合 PackageEntryStream 的 RandomAccess 读取，
    ///    并发提取多个条目也不会互相干扰。
    /// </summary>
    public static class EntryExtractor
    {
        /// <summary>每次进度回调之间至少推进的字节数，避免逐块回调把 UI/日志线程打满。</summary>
        private const long ProgressStep = 16L << 20; // 16 MiB

        /// <summary>
        /// 把单个条目完整复制到 <paramref name="destination"/>，返回复制的字节数。
        /// </summary>
        public static long CopyEntry(
            PackageFileSource source,
            PackageEntry entry,
            Stream destination,
            int bufferSize = Constants.DefaultCopyBufferSize,
            IProgress<long> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            using (var entryStream = source.OpenEntry(entry))
            {
                return CopyStream(entryStream, destination, entry.Length, bufferSize, progress, cancellationToken);
            }
        }

        /// <summary>
        /// 从 <paramref name="source"/> 精确复制 <paramref name="length"/> 个字节到 <paramref name="destination"/>。
        /// </summary>
        public static long CopyStream(
            Stream source,
            Stream destination,
            long length,
            int bufferSize = Constants.DefaultCopyBufferSize,
            IProgress<long> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

            if (bufferSize < Constants.MinimumCopyBufferSize)
                bufferSize = Constants.MinimumCopyBufferSize;

            if (length == 0)
            {
                progress?.Report(0);
                return 0;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                long total = 0;
                var nextReport = ProgressStep;

                while (total < length)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var toRead = (int) Math.Min(buffer.Length, length - total);
                    var read = source.Read(buffer, 0, toRead);

                    if (read <= 0)
                        throw new EndOfStreamException(
                            $"Source ended after {total} of {length} bytes — the entry is truncated");

                    destination.Write(buffer, 0, read);
                    total += read;

                    if (progress != null && total >= nextReport && total < length)
                    {
                        progress.Report(total);
                        nextReport += ProgressStep;
                    }
                }

                progress?.Report(total);
                return total;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
