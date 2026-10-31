using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using RePKG.Application.Package;
using RePKG.TestSupport;
using Xunit;

namespace RePKG.Tests.BigFiles
{
    /// <summary>
    /// 阶段 2 门禁：超大条目必须流式提取，峰值内存不随条目体积线性增长，输出逐字节一致。
    ///
    /// 断言口径（阶段 0 - Q11）：以 <see cref="GC.GetTotalAllocatedBytes(bool)"/> 的增量为**主**断言，
    /// 工作集只作为辅助信息打印（本机 7.9 GB 内存，工作集受 OS 缓存与 GC 策略干扰）。
    ///
    /// 运行：设置 <c>REPKG_BIG_TESTS=1</c>，例如
    ///   $env:REPKG_BIG_TESTS=1; dotnet test tests\RePKG.Tests.BigFiles -c Release
    /// </summary>
    public sealed class LargeEntryExtractionTests
    {
        private const long GiB = 1024L * 1024 * 1024;
        private readonly ITestOutputHelper _output;

        public LargeEntryExtractionTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Extract_Single1GiB_Entry_StreamsWithoutLinearMemoryGrowth()
        {
            if (BigTestGate.Gate("1 GiB 单条目门禁") is { } gateReason) Assert.Skip(gateReason);

            var dir = SyntheticPkgFactory.CreateWorkDirectory("1gib");
            try
            {
                // pkg 1 GiB + 输出 1 GiB
                if (BigTestGate.Disk(dir, (2 * GiB) + (256L * 1024 * 1024)) is { } diskReason) Assert.Skip(diskReason);

                var pkgPath = Path.Combine(dir, "big.pkg");
                var outPath = Path.Combine(dir, "big.out");

                var sw = Stopwatch.StartNew();
                SyntheticPkgFactory.Create(pkgPath, new[]
                {
                    new SyntheticPkgFactory.EntrySpec("video/scene.mp4", GiB)
                });
                _output.WriteLine($"合成 pkg 耗时 {sw.ElapsedMilliseconds} ms，大小 {new FileInfo(pkgPath).Length}");

                byte[] expected;
                sw.Restart();
                expected = SyntheticPkgFactory.ComputeExpectedHash(GiB);
                _output.WriteLine($"预期哈希耗时 {sw.ElapsedMilliseconds} ms");

                using var source = PackageFileSource.Open(pkgPath);
                Assert.Single(source.Entries);
                var entry = source.Entries[0];
                Assert.Equal(GiB, entry.Length);
                Assert.Equal(GiB, source.BodyLength);

                using var peak = new PeakWorkingSetSampler();
                peak.Start();

                long allocatedBytes;
                long copied;
                sw.Restart();

                using (var output = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None,
                           1 << 20, FileOptions.SequentialScan))
                {
                    var before = GC.GetTotalAllocatedBytes(precise: true);
                    copied = EntryExtractor.CopyEntry(source, entry, output);
                    var after = GC.GetTotalAllocatedBytes(precise: true);
                    allocatedBytes = after - before;
                }

                peak.Stop();
                _output.WriteLine($"提取耗时 {sw.ElapsedMilliseconds} ms，分配 {allocatedBytes / 1024.0 / 1024.0:F1} MiB，" +
                                  $"峰值工作集 {peak.PeakBytes / 1024.0 / 1024.0:F1} MiB");

                Assert.Equal(GiB, copied);
                Assert.Equal(GiB, new FileInfo(outPath).Length);

                // 主断言：分配增量必须远小于条目体积（缓冲 1 MiB + 少量对象）
                Assert.True(allocatedBytes < 32L * 1024 * 1024,
                    $"提取 1 GiB 条目分配了 {allocatedBytes} 字节；流式实现应当只分配缓冲区");

                // 逐字节一致
                sw.Restart();
                var actual = SyntheticPkgFactory.ComputeFileHash(outPath);
                _output.WriteLine($"输出哈希耗时 {sw.ElapsedMilliseconds} ms");

                Assert.Equal(BitConverter.ToString(expected), BitConverter.ToString(actual));
            }
            finally
            {
                SyntheticPkgFactory.TryDeleteDirectory(dir);
            }
        }

        [Fact]
        public void Extract_MultiGiB_Package_EntriesDoNotAccumulate()
        {
            if (BigTestGate.Gate("总数据量 ≥ 2 GiB 的门禁") is { } gateReason2) Assert.Skip(gateReason2);

            var dir = SyntheticPkgFactory.CreateWorkDirectory("2gib");
            try
            {
                // 2 个 1.1 GiB 条目 => 总数据量 2.2 GiB；输出走哈希接收器，不落盘
                const long entrySize = (long)(1.1 * GiB);
                if (BigTestGate.Disk(dir, (long)(2.3 * GiB)) is { } diskReason2) Assert.Skip(diskReason2);

                var pkgPath = Path.Combine(dir, "multi.pkg");

                var sw = Stopwatch.StartNew();
                SyntheticPkgFactory.Create(pkgPath, new[]
                {
                    new SyntheticPkgFactory.EntrySpec("a/large.bin", entrySize),
                    new SyntheticPkgFactory.EntrySpec("b/large.bin", entrySize)
                });
                _output.WriteLine($"合成 2.2 GiB pkg 耗时 {sw.ElapsedMilliseconds} ms，大小 {new FileInfo(pkgPath).Length}");

                var expectedA = SyntheticPkgFactory.ComputeExpectedHash(entrySize);
                var filler = new DeterministicFiller();
                var expectedB = HashRange(filler, entrySize, entrySize);

                using var source = PackageFileSource.Open(pkgPath);
                Assert.Equal(2, source.Entries.Count);
                Assert.Equal(2 * entrySize, source.BodyLength);

                using var peak = new PeakWorkingSetSampler();
                peak.Start();

                var before = GC.GetTotalAllocatedBytes(precise: true);
                sw.Restart();

                // 顺序提取两个条目，每次都用新的哈希接收器：任一处累积内存都会体现在分配量上
                byte[] hashA, hashB;
                using (var sinkA = new HashingSink())
                {
                    Assert.Equal(entrySize, EntryExtractor.CopyEntry(source, source.Entries[0], sinkA));
                    hashA = sinkA.FinishHash();
                }

                using (var sinkB = new HashingSink())
                {
                    Assert.Equal(entrySize, EntryExtractor.CopyEntry(source, source.Entries[1], sinkB));
                    hashB = sinkB.FinishHash();
                }

                var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - before;
                peak.Stop();

                _output.WriteLine($"提取 2.2 GiB 耗时 {sw.ElapsedMilliseconds} ms，分配 {allocatedBytes / 1024.0 / 1024.0:F1} MiB，" +
                                  $"峰值工作集 {peak.PeakBytes / 1024.0 / 1024.0:F1} MiB");

                Assert.Equal(BitConverter.ToString(expectedA), BitConverter.ToString(hashA));
                Assert.Equal(BitConverter.ToString(expectedB), BitConverter.ToString(hashB));

                Assert.True(allocatedBytes < 32L * 1024 * 1024,
                    $"提取 2.2 GiB 数据分配了 {allocatedBytes} 字节；内存不应随数据量增长");
            }
            finally
            {
                SyntheticPkgFactory.TryDeleteDirectory(dir);
            }
        }

        private static byte[] HashRange(DeterministicFiller filler, long start, long length)
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
    }

    /// <summary>后台采样进程工作集峰值（仅作信息输出，不作断言）。</summary>
    internal sealed class PeakWorkingSetSampler : IDisposable
    {
        private readonly Process _process = Process.GetCurrentProcess();
        private System.Threading.Timer? _timer;
        private long _peak;

        public long PeakBytes => _peak;

        public void Start()
        {
            _peak = _process.WorkingSet64;
            _timer = new System.Threading.Timer(_ =>
            {
                var ws = _process.WorkingSet64;
                if (ws > _peak) _peak = ws;
            }, null, 50, 50);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            var ws = _process.WorkingSet64;
            if (ws > _peak) _peak = ws;
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _process.Dispose();
        }
    }
}
