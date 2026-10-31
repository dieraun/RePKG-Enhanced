using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace RePKG.Application.Package
{
    /// <summary>
    /// 一个 pkg 条目在包文件中的**只读、可 seek、限长**视图。
    ///
    /// 设计要点（对应阶段 0 的根因修复）：
    ///  - 不持有自己的文件句柄，也不共享一个可变的 Position：底层用
    ///    <see cref="RandomAccess"/> 直接按偏移 read，因此多个条目流可安全并行读取同一个包，
    ///    不会互相破坏文件指针。
    ///  - 读取长度被严格限制在 [start, start+length) 内，越界读取返回 0，
    ///    调用方拿到的永远是精确的条目字节，不会读到相邻条目的数据。
    ///  - Dispose 不会关闭底层句柄，句柄由 <see cref="PackageFileSource"/> 统一管理。
    /// </summary>
    public sealed class PackageEntryStream : Stream
    {
        private readonly SafeFileHandle _handle;
        private readonly long _start;
        private readonly long _length;
        private long _position;
        private bool _disposed;

        internal PackageEntryStream(SafeFileHandle handle, long start, long length)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            if (handle.IsInvalid) throw new ArgumentException("Handle is invalid", nameof(handle));
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

            _handle = handle;
            _start = start;
            _length = length;
        }

        /// <summary>条目在包文件中的绝对起始偏移。</summary>
        public long Start => _start;

        public override bool CanRead => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => false;

        public override long Length
        {
            get
            {
                ThrowIfDisposed();
                return _length;
            }
        }

        public override long Position
        {
            get
            {
                ThrowIfDisposed();
                return _position;
            }
            set
            {
                ThrowIfDisposed();
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        /// <summary>剩余可读字节数。</summary>
        public long Remaining => _length - _position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            return Read(new Span<byte>(buffer, offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            ThrowIfDisposed();

            var remaining = _length - _position;
            if (remaining <= 0 || buffer.IsEmpty)
                return 0;

            var toRead = (int) Math.Min(buffer.Length, remaining);
            var read = RandomAccess.Read(_handle, buffer.Slice(0, toRead), _start + _position);
            _position += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var remaining = _length - _position;
            if (remaining <= 0 || buffer.IsEmpty)
                return 0;

            var toRead = (int) Math.Min(buffer.Length, remaining);
            var read = await RandomAccess
                .ReadAsync(_handle, buffer.Slice(0, toRead), _start + _position, cancellationToken)
                .ConfigureAwait(false);
            _position += read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin)
        {
            ThrowIfDisposed();

            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

            if (target < 0) throw new IOException("Cannot seek before the start of the entry");
            _position = target;
            return _position;
        }

        public override void Flush() { }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // 不关闭 _handle：它属于 PackageFileSource
            _disposed = true;
            base.Dispose(disposing);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PackageEntryStream));
        }
    }
}
