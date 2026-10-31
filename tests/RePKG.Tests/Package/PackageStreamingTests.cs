using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using RePKG.Application.Package;
using RePKG.TestSupport;
using Xunit;

namespace RePKG.Tests.Package
{
    /// <summary>
    /// 阶段 2 门禁的核心单元测试：解析路径不得载入条目字节，提取必须走流式。
    /// </summary>
    public sealed class PackageStreamingTests : IDisposable
    {
        private readonly string _dir;

        public PackageStreamingTests()
        {
            _dir = SyntheticPkgFactory.CreateWorkDirectory("streaming");
        }

        public void Dispose() => SyntheticPkgFactory.TryDeleteDirectory(_dir);

        private string Pkg(string name) => Path.Combine(_dir, name);

        [Fact]
        public void ReadTable_DoesNotPopulateEntryBytes()
        {
            var path = Pkg("table.pkg");
            var entries = new[]
            {
                new SyntheticPkgFactory.EntrySpec("preview.jpg", 64 * 1024),
                new SyntheticPkgFactory.EntrySpec("scene.json", 1024),
                new SyntheticPkgFactory.EntrySpec("video.mp4", 2 * 1024 * 1024)
            };

            SyntheticPkgFactory.Create(path, entries);

            using var source = PackageFileSource.Open(path);

            Assert.Equal(3, source.Entries.Count);
            Assert.Equal(SyntheticPkgFactory.DefaultMagic, source.Package.Magic);

            foreach (var entry in source.Entries)
            {
                Assert.Null(entry.Bytes);       // 关键断言：解析不再填充字节
                Assert.True(entry.Length > 0);
            }

            // 数据区起点 = 头部长度，且等于文件长度减去数据区大小
            Assert.Equal(source.HeaderSize, source.DataStart);
            Assert.Equal(2L * 1024 * 1024 + 64 * 1024 + 1024, source.BodyLength);
        }

        [Fact]
        public void ReadTable_AllocatesFarLessThanPackageSize()
        {
            var path = Pkg("alloc.pkg");
            const long entrySize = 64L * 1024 * 1024;

            SyntheticPkgFactory.Create(path, new[] { new SyntheticPkgFactory.EntrySpec("big.bin", entrySize) });

            // 预热：首次调用会把 JIT 代码、类型初始化、FileStream 内部缓冲等一次性分配算进去，
            // 与"条目大小"无关。测量第二次打开才能真正反映表读取的边际成本。
            using (var warm = PackageFileSource.Open(path))
            {
                Assert.Single(warm.Entries);
            }

            var before = GC.GetTotalAllocatedBytes(precise: true);
            using (var source = PackageFileSource.Open(path))
            {
                Assert.Single(source.Entries);
            }

            var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

            // 只读条目表：分配量应是 KB 级（含 FileStream 的 1 MiB 缓冲），绝不能接近条目大小
            Assert.True(allocated < 8L * 1024 * 1024,
                $"Table-only read allocated {allocated} bytes for a {entrySize}-byte entry");
        }

        [Fact]
        public void OpenEntry_ReturnsExactlyTheEntryBytes()
        {
            var path = Pkg("exact.pkg");
            const long aLen = 1024 * 1024;
            const long bLen = 3 * 1024 * 1024 + 7;

            var filler = new DeterministicFiller();
            var expectedA = HashOf(filler, 0, aLen);
            var expectedB = HashOf(filler, aLen, bLen);

            SyntheticPkgFactory.Create(path, new[]
            {
                new SyntheticPkgFactory.EntrySpec("a.bin", aLen),
                new SyntheticPkgFactory.EntrySpec("b.bin", bLen)
            });

            using var source = PackageFileSource.Open(path);
            var a = source.FindEntry("a.bin");
            var b = source.FindEntry("b.bin");

            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Equal(aLen, a!.Length);
            Assert.Equal(bLen, b!.Length);
            Assert.Equal(0, a.Offset);
            Assert.Equal(aLen, b.Offset);

            Assert.Equal(expectedA, HashOfEntry(source, a));
            Assert.Equal(expectedB, HashOfEntry(source, b));
        }

        [Fact]
        public void OpenEntry_ClampsReadsToEntryBoundary()
        {
            var path = Pkg("clamp.pkg");
            SyntheticPkgFactory.Create(path, new[]
            {
                new SyntheticPkgFactory.EntrySpec("first.bin", 4096),
                new SyntheticPkgFactory.EntrySpec("second.bin", 4096)
            });

            using var source = PackageFileSource.Open(path);
            var first = source.FindEntry("first.bin")!;
            using var stream = source.OpenEntry(first);

            Assert.Equal(4096, stream.Length);

            var buffer = new byte[8192];
            var read = 0;
            while (read < 4096)
            {
                var n = stream.Read(buffer, read, buffer.Length - read);
                Assert.True(n > 0);
                read += n;
            }

            // 越界读取必须返回 0，绝不能读到第二个条目的数据
            Assert.Equal(0, stream.Read(buffer, 0, buffer.Length));
            Assert.Equal(0, stream.Read(buffer, 0, buffer.Length));
        }

        [Fact]
        public void OpenEntry_SupportsParallelReadsWithSingleHandle()
        {
            var path = Pkg("parallel.pkg");
            const long len = 2 * 1024 * 1024;
            var filler = new DeterministicFiller();

            SyntheticPkgFactory.Create(path, new[]
            {
                new SyntheticPkgFactory.EntrySpec("one.bin", len),
                new SyntheticPkgFactory.EntrySpec("two.bin", len)
            });

            using var source = PackageFileSource.Open(path);
            var one = source.FindEntry("one.bin")!;
            var two = source.FindEntry("two.bin")!;

            var expectedOne = HashOf(filler, 0, len);
            var expectedTwo = HashOf(filler, len, len);

            // 交替读取两个条目：若底层共享可变 Position，结果必然错乱
            using var s1 = source.OpenEntry(one);
            using var s2 = source.OpenEntry(two);

            using var h1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var h2 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buf1 = new byte[64 * 1024];
            var buf2 = new byte[32 * 1024];

            long r1 = 0, r2 = 0;
            while (r1 < len || r2 < len)
            {
                // 两个流各自读到自己的 EOF：缓冲区大小不同，不能共用一个计数
                if (r1 < len)
                {
                    var n1 = s1.Read(buf1, 0, (int) Math.Min(buf1.Length, len - r1));
                    Assert.True(n1 > 0);
                    h1.AppendData(buf1, 0, n1);
                    r1 += n1;
                }

                if (r2 < len)
                {
                    var n2 = s2.Read(buf2, 0, (int) Math.Min(buf2.Length, len - r2));
                    Assert.True(n2 > 0);
                    h2.AppendData(buf2, 0, n2);
                    r2 += n2;
                }
            }

            Assert.Equal(expectedOne, h1.GetHashAndReset());
            Assert.Equal(expectedTwo, h2.GetHashAndReset());
        }

        [Fact]
        public void PackageWriter_RoundTripsNonAsciiPaths()
        {
            // 原版 WriteStringI32Size 写的是字符数而非字节数，非 ASCII 路径回读会错
            var path = Pkg("roundtrip.pkg");
            var entry = new RePKG.Core.Package.PackageEntry
            {
                FullPath = "预览/场景 图.jpg",
                Bytes = new byte[] { 1, 2, 3, 4, 5 }
            };

            var package = new RePKG.Core.Package.Package { Magic = SyntheticPkgFactory.DefaultMagic };
            package.Entries.Add(entry);

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fs))
            {
                new PackageWriter().WriteTo(writer, package);
            }

            using var source = PackageFileSource.Open(path);
            Assert.Single(source.Entries);
            Assert.Equal("预览/场景 图.jpg", source.Entries[0].FullPath);
            Assert.Equal(5, source.Entries[0].Length);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, SyntheticPkgFactory.ReadEntryBytes(path, "预览/场景 图.jpg"));
        }

        [Fact]
        public void EntryExtractor_ReportsProgressAndCopiesExactly()
        {
            var path = Pkg("progress.pkg");
            const long len = 64L * 1024 * 1024 + 123;
            SyntheticPkgFactory.Create(path, new[] { new SyntheticPkgFactory.EntrySpec("p.bin", len) });

            using var source = PackageFileSource.Open(path);
            var entry = source.Entries[0];

            var reports = new List<long>();
            using var output = new MemoryStream();
            var copied = EntryExtractor.CopyEntry(source, entry, output, progress: new SyncProgress(reports));

            Assert.Equal(len, copied);
            Assert.Equal(len, output.Length);

            // 16 MiB 一次 => 至少 4 次中间进度 + 1 次最终进度
            Assert.True(reports.Count >= 5, $"expected >=5 progress reports, got {reports.Count}");
            Assert.Equal(len, reports[^1]);
        }

        [Fact]
        public void EntryExtractor_AllocationsDoNotScaleWithEntrySize()
        {
            var path = Pkg("alloc-extract.pkg");
            const long len = 64L * 1024 * 1024;
            SyntheticPkgFactory.Create(path, new[] { new SyntheticPkgFactory.EntrySpec("p.bin", len) });

            using var source = PackageFileSource.Open(path);
            var entry = source.Entries[0];

            using var sink = Stream.Null;

            var before = GC.GetTotalAllocatedBytes(precise: true);
            var copied = EntryExtractor.CopyEntry(source, entry, sink);
            var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

            Assert.Equal(len, copied);

            // 1 MiB ArrayPool 缓冲 + 少量对象；远小于条目本身
            Assert.True(allocated < 16L * 1024 * 1024,
                $"Copying {len} bytes allocated {allocated} bytes (buffer should come from ArrayPool)");
        }

        [Fact]
        public void EntryExtractor_SupportsCancellation()
        {
            var path = Pkg("cancel.pkg");
            const long len = 32L * 1024 * 1024;
            SyntheticPkgFactory.Create(path, new[] { new SyntheticPkgFactory.EntrySpec("p.bin", len) });

            using var source = PackageFileSource.Open(path);
            var entry = source.Entries[0];

            using var cts = new System.Threading.CancellationTokenSource();
            using var sink = Stream.Null;

            using var progress = new CancellingProgress(cts);

            Assert.Throws<OperationCanceledException>(() =>
                EntryExtractor.CopyEntry(source, entry, sink, progress: progress, cancellationToken: cts.Token));
        }

        private static byte[] HashOf(DeterministicFiller filler, long start, long length)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1 << 20];
            long done = 0;
            while (done < length)
            {
                var chunk = (int)Math.Min(buffer.Length, length - done);
                filler.Fill(buffer.AsSpan(0, chunk), start + done);
                hash.AppendData(buffer, 0, chunk);
                done += chunk;
            }

            return hash.GetHashAndReset();
        }

        private static byte[] HashOfEntry(PackageFileSource source, RePKG.Core.Package.PackageEntry entry)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var stream = source.OpenEntry(entry);
            var buffer = new byte[1 << 20];
            int n;
            while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, n);
            return hash.GetHashAndReset();
        }

        private sealed class SyncProgress : IProgress<long>
        {
            private readonly List<long> _sink;
            public SyncProgress(List<long> sink) => _sink = sink;
            public void Report(long value) => _sink.Add(value);
        }

        private sealed class CancellingProgress : IProgress<long>, IDisposable
        {
            private readonly System.Threading.CancellationTokenSource _cts;
            public CancellingProgress(System.Threading.CancellationTokenSource cts) => _cts = cts;
            public void Report(long value) => _cts.Cancel();
            public void Dispose() => _cts.Dispose();
        }
    }
}
