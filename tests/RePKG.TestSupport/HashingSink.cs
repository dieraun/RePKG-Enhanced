using System;
using System.IO;
using System.Security.Cryptography;

namespace RePKG.TestSupport
{
    /// <summary>
    /// 只计算哈希、不落盘的 Stream 目标。用于大文件测试中"读一遍就校验"的场景，
    /// 避免为了校验再写一份 GB 级输出。
    /// </summary>
    public sealed class HashingSink : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public long BytesWritten { get; private set; }
        public long WriteCalls { get; private set; }
        public long LargestWrite { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
            => Write(new ReadOnlySpan<byte>(buffer, offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _hash.AppendData(buffer);
            BytesWritten += buffer.Length;
            WriteCalls++;
            if (buffer.Length > LargestWrite) LargestWrite = buffer.Length;
        }

        public byte[] FinishHash() => _hash.GetHashAndReset();

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _hash.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 大文件门禁的开关与前置检查。
    /// 刻意不引用测试框架：本库被多个测试项目共享，返回"跳过原因"由调用方决定如何处理。
    /// </summary>
    public static class BigTestGate
    {
        public const string EnvVar = "REPKG_BIG_TESTS";

        public static bool Enabled =>
            string.Equals(Environment.GetEnvironmentVariable(EnvVar), "1", StringComparison.Ordinal);

        /// <summary>返回 null 表示可以继续；否则返回应跳过的原因。</summary>
        public static string? Gate(string label)
        {
            if (!Enabled)
                return $"大文件门禁未启用（设置 {EnvVar}=1 运行）：{label}";

            return null;
        }

        /// <summary>检查工作目录所在卷的可用空间，不足则返回跳过原因而不是写到磁盘满。</summary>
        public static string? Disk(string directory, long requiredBytes)
        {
            Directory.CreateDirectory(directory);
            var root = Path.GetPathRoot(Path.GetFullPath(directory)) ?? "C:\\";
            var drive = new DriveInfo(root);

            if (drive.AvailableFreeSpace < requiredBytes)
            {
                return $"磁盘空间不足：{root} 可用 {drive.AvailableFreeSpace / (1024 * 1024)} MiB，" +
                       $"需要 {requiredBytes / (1024 * 1024)} MiB";
            }

            return null;
        }
    }
}
