using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RePKG.Shared.Export;
using RePKG.TestSupport;
using Xunit;

namespace RePKG.Tests.Shared.Export
{
    public sealed class ExportQueueTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _out;

        public ExportQueueTests()
        {
            _dir = SyntheticPkgFactory.CreateWorkDirectory("queue");
            _out = Path.Combine(_dir, "out");
            Directory.CreateDirectory(_out);
        }

        private string Pkg(string name = "a.pkg") => Path.Combine(_dir, name);

        private static ExportOptions Options(Action<ExportOptions>? configure = null)
        {
            var options = new ExportOptions { WriteLog = false, Concurrency = 1 };
            configure?.Invoke(options);
            return options;
        }

        [Fact]
        public async Task Exports_All_Entries_With_Original_Structure()
        {
            var pkg = Pkg();
            SyntheticPkgFactory.Create(pkg, new[]
            {
                new SyntheticPkgFactory.EntrySpec("tex/a.png", 1024),
                new SyntheticPkgFactory.EntrySpec("media/movie.mp4", 4096),
                new SyntheticPkgFactory.EntrySpec("readme.txt", 32)
            });

            await using var queue = new ExportQueue(Options());
            var result = await queue.EnqueueAsync(pkg, _out);

            Assert.Null(result.FatalError);
            Assert.Equal(3, result.Succeeded);
            Assert.Equal(0, result.Failed);

            var expected = new[] { "tex/a.png", "media/movie.mp4", "readme.txt" };
            foreach (var relative in expected)
            {
                var file = Path.Combine(_out, relative.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(file), file);
                Assert.Equal(SyntheticPkgFactory.ReadEntryBytes(pkg, relative), File.ReadAllBytes(file));
            }

            Assert.Empty(Directory.GetFiles(_out, "*.tmp", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task Skipped_Existing_Files_Are_Not_Overwritten_By_Default()
        {
            var pkg = Pkg();
            SyntheticPkgFactory.Create(pkg, new[] { new SyntheticPkgFactory.EntrySpec("keep.txt", 64) });

            var target = Path.Combine(_out, "keep.txt");
            File.WriteAllText(target, "sentinel");

            await using var queue = new ExportQueue(Options());
            var result = await queue.EnqueueAsync(pkg, _out);

            Assert.Equal(1, result.Skipped);
            Assert.Equal(0, result.Succeeded);
            Assert.Equal("sentinel", File.ReadAllText(target));
        }

        [Fact]
        public async Task Overwrite_Replaces_Existing_Files()
        {
            var pkg = Pkg();
            SyntheticPkgFactory.Create(pkg, new[] { new SyntheticPkgFactory.EntrySpec("keep.txt", 64) });

            var target = Path.Combine(_out, "keep.txt");
            File.WriteAllText(target, "sentinel");

            await using var queue = new ExportQueue(Options(o => o.Overwrite = true));
            var result = await queue.EnqueueAsync(pkg, _out);

            Assert.Equal(1, result.Succeeded);
            Assert.Equal(SyntheticPkgFactory.ReadEntryBytes(pkg, "keep.txt"), File.ReadAllBytes(target));
        }

        [Fact]
        public async Task Corrupt_Package_Fails_Without_Affecting_Other_Jobs()
        {
            var good = Pkg("good.pkg");
            SyntheticPkgFactory.Create(good, new[] { new SyntheticPkgFactory.EntrySpec("ok.bin", 256) });

            var bad = Pkg("bad.pkg");
            File.WriteAllText(bad, "this is not a pkg at all");

            await using var queue = new ExportQueue(Options());
            var results = await queue.EnqueueManyAsync(new[] { bad, good }, _out);
            var summary = await queue.CompleteAsync();

            Assert.Equal(2, results.Count);
            Assert.Equal(2, summary.Packages);
            Assert.NotNull(results.Single(r => r.PackagePath == bad).FatalError);
            Assert.Equal(1, results.Single(r => r.PackagePath == good).Succeeded);
            Assert.False(summary.IsSuccess);
            Assert.True(File.Exists(Path.Combine(_out, "ok.bin")));
        }

        [Fact]
        public async Task Extension_Filter_Exports_Only_Matching_Entries()
        {
            var pkg = Pkg();
            SyntheticPkgFactory.Create(pkg, new[]
            {
                new SyntheticPkgFactory.EntrySpec("a.tex", 128),
                new SyntheticPkgFactory.EntrySpec("b.png", 128),
                new SyntheticPkgFactory.EntrySpec("c.mp4", 128)
            });

            await using var queue = new ExportQueue(Options(o => o.OnlyExtensions = new[] { "tex" }));
            var result = await queue.EnqueueAsync(pkg, _out);

            Assert.Equal(1, result.Succeeded);
            Assert.True(File.Exists(Path.Combine(_out, "a.tex")));
            Assert.False(File.Exists(Path.Combine(_out, "b.png")));
            Assert.False(File.Exists(Path.Combine(_out, "c.mp4")));
        }

        [Fact]
        public async Task Ignore_Filter_Wins_Over_Only_Filter()
        {
            var pkg = Pkg();
            SyntheticPkgFactory.Create(pkg, new[]
            {
                new SyntheticPkgFactory.EntrySpec("a.tex", 128),
                new SyntheticPkgFactory.EntrySpec("b.png", 128),
                new SyntheticPkgFactory.EntrySpec("c.mp4", 128)
            });

            await using var queue = new ExportQueue(Options(o =>
            {
                o.OnlyExtensions = new[] { "tex", "png" };
                o.IgnoreExtensions = new[] { "png" };
            }));

            var result = await queue.EnqueueAsync(pkg, _out);

            Assert.Equal(1, result.Succeeded);
            Assert.True(File.Exists(Path.Combine(_out, "a.tex")));
            Assert.False(File.Exists(Path.Combine(_out, "b.png")));
            Assert.False(File.Exists(Path.Combine(_out, "c.mp4")));
        }

        [Fact]
        public async Task SingleDirectory_Flattens_Names_And_Reports_Collisions()
        {
            var pkg = Pkg();
            SyntheticPkgFactory.Create(pkg, new[]
            {
                new SyntheticPkgFactory.EntrySpec("a/icon.png", 64),
                new SyntheticPkgFactory.EntrySpec("a_icon.png", 64)
            });

            await using var queue = new ExportQueue(Options(o => o.SingleDirectory = true));
            var result = await queue.EnqueueAsync(pkg, _out);

            // "a/icon.png" 压平为 "a_icon.png"，与真实条目 "a_icon.png" 同名 → 只写第一个，第二个计为冲突跳过
            Assert.Equal(1, result.Succeeded);
            Assert.Equal(1, result.Skipped);
            Assert.True(File.Exists(Path.Combine(_out, "a_icon.png")));
            Assert.False(Directory.Exists(Path.Combine(_out, "a")));
        }

        [Fact]
        public async Task Entry_With_Traversal_Path_Is_Skipped_And_Nothing_Escapes()
        {
            var pkg = Pkg();
            SyntheticPkgFactory.Create(pkg, new[]
            {
                new SyntheticPkgFactory.EntrySpec("../escape.bin", 64),
                new SyntheticPkgFactory.EntrySpec("safe.bin", 64)
            });

            Directory.CreateDirectory(_out);

            await using var queue = new ExportQueue(Options());
            var result = await queue.EnqueueAsync(pkg, _out);

            Assert.Equal(1, result.Succeeded);
            Assert.Equal(1, result.Skipped);
            Assert.True(File.Exists(Path.Combine(_out, "safe.bin")));
            Assert.False(File.Exists(Path.Combine(_dir, "escape.bin")));
        }

        [Fact]
        public async Task Writes_Jsonl_Log_With_Entry_And_Summary_Events()
        {
            var pkg = Pkg();
            SyntheticPkgFactory.Create(pkg, new[] { new SyntheticPkgFactory.EntrySpec("one.bin", 64) });

            string? logPath;

            await using (var queue = new ExportQueue(Options(o => o.WriteLog = true)))
            {
                var result = await queue.EnqueueAsync(pkg, _out);
                logPath = result.LogPath;
            }

            Assert.NotNull(logPath);
            var lines = File.ReadAllLines(logPath!);
            Assert.Contains(lines, l => l.Contains("\"event\":\"package.start\""));
            Assert.Contains(lines, l => l.Contains("\"event\":\"entry.ok\""));
            Assert.Contains(lines, l => l.Contains("\"event\":\"package.done\""));
        }

        [Fact]
        public async Task Progress_Events_Cover_Every_Entry()
        {
            var pkg = Pkg();
            SyntheticPkgFactory.Create(pkg, new[]
            {
                new SyntheticPkgFactory.EntrySpec("a.bin", 32 * 1024),
                new SyntheticPkgFactory.EntrySpec("b.bin", 64 * 1024)
            });

            var updates = new List<ExportProgress>();

            await using var queue = new ExportQueue(Options());
            queue.ProgressChanged += p => { lock (updates) updates.Add(p); };

            await queue.EnqueueAsync(pkg, _out);
            await queue.CompleteAsync();

            lock (updates)
            {
                Assert.NotEmpty(updates);
                Assert.Contains(updates, u => u.CurrentEntry == "b.bin");
                Assert.Equal(2, updates.Max(u => u.EntriesTotal));
            }
        }

        [Fact]
        public async Task Cancel_MidEntry_Leaves_No_Partial_File_And_No_Temp()
        {
            var pkg = Pkg();
            const long size = 64L * 1024 * 1024;
            SyntheticPkgFactory.Create(pkg, new[] { new SyntheticPkgFactory.EntrySpec("big.bin", size) });

            await using var queue = new ExportQueue(Options());

            // 首个进度回调（16 MiB 边界）触发取消，确保是"复制中途"而不是复制完成后
            queue.ProgressChanged += p =>
            {
                if (p.EntryBytesDone >= 16L * 1024 * 1024)
                    queue.Control.Cancel();
            };

            var result = await queue.EnqueueAsync(pkg, _out);

            Assert.True(result.Canceled >= 1, $"期望取消，实际 {result.Canceled}");
            Assert.Equal(0, result.Succeeded);
            Assert.False(File.Exists(Path.Combine(_out, "big.bin")));
            Assert.Empty(Directory.GetFiles(_out, "*.tmp", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task Paused_Queue_Does_Not_Start_Work_Until_Resumed()
        {
            var pkg = Pkg();
            SyntheticPkgFactory.Create(pkg, new[] { new SyntheticPkgFactory.EntrySpec("p.bin", 128 * 1024) });

            await using var queue = new ExportQueue(Options());
            queue.Control.Pause();

            var pending = queue.EnqueueAsync(pkg, _out);

            await Task.Delay(400);
            Assert.False(File.Exists(Path.Combine(_out, "p.bin")), "暂停期间不应写出任何文件");

            queue.Control.Resume();
            var result = await pending;

            Assert.Equal(1, result.Succeeded);
            Assert.True(File.Exists(Path.Combine(_out, "p.bin")));
        }

        [Fact]
        public async Task Concurrency_Two_Processes_All_Jobs()
        {
            var paths = new List<string>();

            for (var i = 0; i < 4; i++)
            {
                var pkg = Pkg($"c{i}.pkg");
                SyntheticPkgFactory.Create(pkg, new[] { new SyntheticPkgFactory.EntrySpec($"f{i}.bin", 256 * 1024) });
                paths.Add(pkg);
            }

            await using var queue = new ExportQueue(Options(o => { o.Concurrency = 2; o.QueueCapacity = 2; }));

            var results = await queue.EnqueueManyAsync(paths, _out);
            var summary = await queue.CompleteAsync();

            Assert.All(results, r => Assert.Null(r.FatalError));
            Assert.Equal(4, results.Sum(r => r.Succeeded));
            Assert.Equal(4, summary.Packages);
            Assert.True(summary.IsSuccess);
        }

        [Fact]
        public async Task Summary_Accumulates_Failures_Across_Packages()
        {
            var good = Pkg("s-good.pkg");
            SyntheticPkgFactory.Create(good, new[] { new SyntheticPkgFactory.EntrySpec("g.bin", 128) });

            var badPkg = Pkg("s-bad.pkg");

            // 头部合法、条目声明 4 MiB 但只有 1 KiB 数据：整包在打开时就被判为截断
            var header = SyntheticPkgFactory.BuildHeader(SyntheticPkgFactory.DefaultMagic, new[]
            {
                new SyntheticPkgFactory.EntrySpec("claimed.bin", 4 * 1024 * 1024)
            });

            using (var fs = new FileStream(badPkg, FileMode.Create, FileAccess.Write))
            {
                fs.Write(header, 0, header.Length);
                fs.Write(new byte[1024], 0, 1024);
            }

            await using var queue = new ExportQueue(Options());
            await queue.EnqueueManyAsync(new[] { good, badPkg }, _out);
            var summary = await queue.CompleteAsync();

            Assert.Equal(1, summary.Failed);
            Assert.False(summary.IsSuccess);
            Assert.Equal(1, summary.Succeeded);
        }

        [Fact]
        public async Task WriteManifest_EmitsEntryTableJson()
        {
            var pkg = Pkg("manifest.pkg");
            SyntheticPkgFactory.CreateFromFiles(pkg, new Dictionary<string, byte[]>
            {
                ["a/one.bin"] = new byte[] { 1, 2, 3 },
                ["two.txt"] = Encoding.UTF8.GetBytes("hello")
            });

            await using var queue = new ExportQueue(Options(o => o.WriteManifest = true));
            var result = await queue.EnqueueAsync(pkg, _out);

            Assert.Null(result.FatalError);

            var manifestPath = Path.Combine(_out, "manifest.json");
            Assert.True(File.Exists(manifestPath));

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;

            Assert.Equal(2, root.GetProperty("entryCount").GetInt32());
            Assert.Equal(8, root.GetProperty("totalBytes").GetInt64());

            var entries = root.GetProperty("entries").EnumerateArray().ToList();
            Assert.Equal(new[] { "a/one.bin", "two.txt" }, entries.Select(e => e.GetProperty("fullPath").GetString()));
            Assert.Equal(0, entries[0].GetProperty("offset").GetInt64());
            Assert.Equal(3, entries[0].GetProperty("length").GetInt64());
            Assert.Equal(3, entries[1].GetProperty("offset").GetInt64());
            Assert.Equal(5, entries[1].GetProperty("length").GetInt64());
        }

        [Fact]
        public async Task Manifest_IsNotWritten_ByDefault()
        {
            var pkg = Pkg("nomanifest.pkg");
            SyntheticPkgFactory.Create(pkg, new[] { new SyntheticPkgFactory.EntrySpec("a.bin", 16) });

            await using var queue = new ExportQueue(Options());
            await queue.EnqueueAsync(pkg, _out);

            Assert.False(File.Exists(Path.Combine(_out, "manifest.json")));
        }

        [Fact]
        public void Options_Validate_Rejects_Invalid_Values()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Options(o => o.Concurrency = 0).Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => Options(o => o.QueueCapacity = 0).Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => Options(o => o.CopyBufferSize = 16).Validate());
            Assert.Throws<ArgumentException>(() => Options(o => o.OutputDirectory = "  ").Validate());
        }

        public void Dispose()
        {
            SyntheticPkgFactory.TryDeleteDirectory(_dir);
        }
    }
}
