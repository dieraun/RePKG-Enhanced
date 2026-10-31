using System;
using System.IO;
using System.Text;
using RePKG.Application.Package;
using RePKG.TestSupport;
using Xunit;

namespace RePKG.Tests.Package
{
    /// <summary>
    /// 截断/畸形包必须**可见地失败**。
    /// 原版的两条静默路径：<c>ReadBytes</c> 返回短数组、<c>Math.Min(size, maxLength)</c> 截断路径名。
    /// 这两条都已被去掉（见 Extensions.ReadStringI32Size 与 EntryExtractor）。
    /// </summary>
    public sealed class TruncatedPackageTests : IDisposable
    {
        private readonly string _dir;

        public TruncatedPackageTests()
        {
            _dir = SyntheticPkgFactory.CreateWorkDirectory("truncated");
        }

        public void Dispose() => SyntheticPkgFactory.TryDeleteDirectory(_dir);

        [Fact]
        public void EntryClaimingMoreDataThanFile_IsRejectedAtOpen()
        {
            var path = Path.Combine(_dir, "oversize.pkg");
            var header = SyntheticPkgFactory.BuildHeader(SyntheticPkgFactory.DefaultMagic, new[]
            {
                new SyntheticPkgFactory.EntrySpec("claimed.bin", 10 * 1024 * 1024)
            });

            File.WriteAllBytes(path, header); // 声明 10 MiB，实际一个字节数据都没有

            var ex = Assert.Throws<InvalidDataException>(() => PackageFileSource.Open(path));
            Assert.Contains("truncated or corrupt", ex.Message);
        }

        [Fact]
        public void EntryClaimingMoreDataThanFile_FailsEvenWhenRangeValidationDisabled()
        {
            var path = Path.Combine(_dir, "oversize2.pkg");
            var header = SyntheticPkgFactory.BuildHeader(SyntheticPkgFactory.DefaultMagic, new[]
            {
                new SyntheticPkgFactory.EntrySpec("claimed.bin", 4 * 1024 * 1024)
            });

            // 写入头部 + 一半数据
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                fs.Write(header, 0, header.Length);
                fs.Write(new byte[2 * 1024 * 1024], 0, 2 * 1024 * 1024);
            }

            using var source = PackageFileSource.Open(path, validateEntryRanges: false);
            var entry = source.Entries[0];

            // OpenEntry 自身会再次校验，因此仍需抛错
            Assert.Throws<InvalidDataException>(() => source.OpenEntry(entry));
        }

        [Fact]
        public void HugeOffset_IsReadAsUnsigned()
        {
            // 偏移 = 3 GiB：原版 ReadInt32 会得到负数，随后 Seek 抛 ArgumentOutOfRange 或读到错误位置
            var path = Path.Combine(_dir, "huge-offset.pkg");
            var header = BuildRawHeader("huge.bin", offset: 3u * 1024 * 1024 * 1024, length: 16);

            File.WriteAllBytes(path, header);

            var ex = Assert.Throws<InvalidDataException>(() => PackageFileSource.Open(path));
            Assert.Contains("3221225472", ex.Message); // 3 GiB 的无符号表示
        }

        [Fact]
        public void NegativeStringSize_IsRejected()
        {
            var path = Path.Combine(_dir, "negative-string.pkg");
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                var magic = Encoding.UTF8.GetBytes(SyntheticPkgFactory.DefaultMagic);
                w.Write(magic.Length);
                w.Write(magic);
                w.Write(1);      // 1 个条目
                w.Write(-5);     // 路径长度 = -5
            }

            File.WriteAllBytes(path, ms.ToArray());

            var ex = Assert.Throws<InvalidDataException>(() => PackageFileSource.Open(path));
            Assert.Contains("cannot be negative", ex.Message);
        }

        [Fact]
        public void OverlongEntryPath_IsRejectedNotSilentlyTruncated()
        {
            var path = Path.Combine(_dir, "long-path.pkg");
            var longName = new string('a', 1100) + ".bin";
            var header = SyntheticPkgFactory.BuildHeader(SyntheticPkgFactory.DefaultMagic, new[]
            {
                new SyntheticPkgFactory.EntrySpec(longName, 8)
            });

            File.WriteAllBytes(path, header);

            var ex = Assert.Throws<InvalidDataException>(() => PackageFileSource.Open(path));
            Assert.Contains("sanity limit of 1024", ex.Message);
        }

        private static byte[] BuildRawHeader(string fullPath, uint offset, uint length)
        {
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                var magic = Encoding.UTF8.GetBytes(SyntheticPkgFactory.DefaultMagic);
                w.Write(magic.Length);
                w.Write(magic);
                w.Write(1);
                var pathBytes = Encoding.UTF8.GetBytes(fullPath);
                w.Write(pathBytes.Length);
                w.Write(pathBytes);
                w.Write(offset);
                w.Write(length);
            }

            return ms.ToArray();
        }
    }
}
