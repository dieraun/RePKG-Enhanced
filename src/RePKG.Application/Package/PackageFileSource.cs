using System;
using System.Collections.Generic;
using System.IO;
using RePKG.Core.Package;

namespace RePKG.Application.Package
{
    /// <summary>
    /// 一个已打开的 pkg 文件的**唯一**读句柄 + 已解析的条目表。
    ///
    /// 生命周期即"处理这个包"的全过程：打开 → 读表（只读头部，不读数据区）→
    /// 逐个条目流式读取 → 关闭。批量处理时由上层保证同一时刻只打开有限个包
    /// （RePKG.Shared 的导出队列把并发上限压到 1–2）。
    ///
    /// 打开方式固定为 <c>FileAccess.Read + FileShare.Read</c>：只读，且不阻止其它进程同时读取，
    /// 原版用 <c>File.Open</c>(ReadWrite) 语义会独占并可能锁死用户正在用的文件。
    /// </summary>
    public sealed class PackageFileSource : IDisposable
    {
        private readonly FileStream _stream;
        private bool _disposed;

        private PackageFileSource(string path, FileStream stream, Core.Package.Package package, long dataStart)
        {
            Path = path;
            _stream = stream;
            Package = package;
            DataStart = dataStart;
        }

        /// <summary>包文件路径。</summary>
        public string Path { get; }

        /// <summary>条目表（不含条目字节）。</summary>
        public Core.Package.Package Package { get; }

        /// <summary>条目列表的快捷入口。</summary>
        public IReadOnlyList<PackageEntry> Entries => Package.Entries;

        /// <summary>数据区在文件中的起始字节偏移。</summary>
        public long DataStart { get; }

        /// <summary>包的头部（魔数 + 条目表）字节数。</summary>
        public int HeaderSize => Package.HeaderSize;

        /// <summary>包文件总长度。</summary>
        public long FileLength => _stream.Length;

        /// <summary>数据区总长度（文件长度 - 数据区起点）。</summary>
        public long BodyLength => _stream.Length - DataStart;

        public static PackageFileSource Open(string path, bool validateEntryRanges = true)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is empty", nameof(path));

            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: Constants.DefaultCopyBufferSize,
                FileOptions.SequentialScan);

            try
            {
                var reader = new PackageReader { ValidateEntryRanges = validateEntryRanges };
                Core.Package.Package package;
                using (var binaryReader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    package = reader.ReadFrom(binaryReader);
                }

                var dataStart = stream.Position;
                return new PackageFileSource(path, stream, package, dataStart);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 打开某个条目的只读流。流的长度严格等于 <see cref="PackageEntry.Length"/>，
        /// 与文件指针无关，可安全并行使用。
        /// </summary>
        public PackageEntryStream OpenEntry(PackageEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            ThrowIfDisposed();

            if (entry.Offset < 0 || entry.Length < 0)
                throw new InvalidDataException($"Entry `{entry.FullPath}` has a negative offset/length");

            if (entry.Offset + entry.Length > BodyLength)
                throw new InvalidDataException(
                    $"Entry `{entry.FullPath}` (offset {entry.Offset}, length {entry.Length}) " +
                    $"exceeds the body size of {BodyLength} bytes — the package is truncated or corrupt");

            return new PackageEntryStream(_stream.SafeFileHandle, DataStart + entry.Offset, entry.Length);
        }

        /// <summary>按完整路径查找条目（区分大小写，与原版行为一致）。</summary>
        public PackageEntry FindEntry(string fullPath)
        {
            foreach (var entry in Package.Entries)
            {
                if (string.Equals(entry.FullPath, fullPath, StringComparison.Ordinal))
                    return entry;
            }

            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stream.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PackageFileSource));
        }
    }
}
