using System;
using System.IO;

namespace RePKG.Shared.IO
{
    /// <summary>
    /// 原子写入：先写同目录下的临时文件，成功后一次性改名替换目标文件。
    ///
    /// 保证：
    /// <list type="bullet">
    /// <item>进程被中途杀掉、磁盘写满、解码抛异常 → 目标文件**不存在或仍是旧内容**，不会出现半截文件</item>
    /// <item>临时文件与目标同目录（同卷），改名是原子操作</item>
    /// <item><see cref="Commit"/> 前显式 <c>FlushToDisk</c>，避免掉电后留下长度为 0 的"成功"文件</item>
    /// <item>未提交就 <see cref="Dispose"/> → 删除临时文件，不留垃圾</item>
    /// </list>
    /// </summary>
    public sealed class AtomicFileWriter : IDisposable
    {
        private readonly string _targetPath;
        private readonly string _tempPath;
        private FileStream? _stream;
        private bool _committed;
        private bool _disposed;

        private AtomicFileWriter(string targetPath, string tempPath, FileStream stream)
        {
            _targetPath = targetPath;
            _tempPath = tempPath;
            _stream = stream;
        }

        /// <summary>写入目标数据的流。只写不读，顺序写。</summary>
        public Stream Stream => _stream ?? throw new ObjectDisposedException(nameof(AtomicFileWriter));

        /// <summary>已写入的字节数。</summary>
        public long BytesWritten => _stream?.Length ?? 0;

        /// <summary>临时文件绝对路径（诊断用）。</summary>
        public string TempPath => _tempPath;

        /// <summary>目标文件绝对路径。</summary>
        public string TargetPath => _targetPath;

        /// <summary>
        /// 在 <paramref name="targetPath"/> 旁创建临时文件并返回写入器。
        /// 目标目录不存在时会创建。
        /// </summary>
        public static AtomicFileWriter Create(string targetPath, int bufferSize = 1 << 16)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentException("目标路径为空", nameof(targetPath));

            var target = Path.GetFullPath(targetPath);
            var directory = Path.GetDirectoryName(target);

            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException($"目标路径没有父目录：'{targetPath}'", nameof(targetPath));

            Directory.CreateDirectory(directory);

            // 同目录 + 随机后缀：既能原子改名，也不会与并发任务互相覆盖
            var temp = Path.Combine(
                directory,
                "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp");

            var stream = new FileStream(
                temp,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize,
                FileOptions.SequentialScan);

            return new AtomicFileWriter(target, temp, stream);
        }

        /// <summary>
        /// 落盘并原子替换目标文件。调用后写入器不可再用（Dispose 只清理状态）。
        /// </summary>
        public void Commit()
        {
            ThrowIfDisposed();

            if (_committed)
                throw new InvalidOperationException("已经提交过了");

            var stream = _stream!;

            // 先把数据刷到磁盘，再改名：否则掉电可能留下 0 字节的"最终文件"
            stream.Flush(flushToDisk: true);
            stream.Dispose();
            _stream = null;

            try
            {
                File.Move(_tempPath, _targetPath, overwrite: true);
                _committed = true;
            }
            catch
            {
                TryDelete(_tempPath);
                throw;
            }
        }

        /// <summary>放弃写入：关闭并删除临时文件，目标文件保持原样。</summary>
        public void Abort()
        {
            if (_disposed)
                return;

            _stream?.Dispose();
            _stream = null;
            TryDelete(_tempPath);
            _disposed = true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            if (!_committed)
            {
                _stream?.Dispose();
                _stream = null;
                TryDelete(_tempPath);
            }

            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AtomicFileWriter));
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // 清理失败不该掩盖原始异常
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
