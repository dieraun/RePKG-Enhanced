using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RePKG.Shared.Logging;
using Xunit;

namespace RePKG.Tests.Shared.Logging
{
    public sealed class JsonlExportLogTests : IDisposable
    {
        private readonly string _dir;

        public JsonlExportLogTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "repkg-log-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_dir);
        }

        [Fact]
        public void Writes_One_Json_Object_Per_Line()
        {
            var path = Path.Combine(_dir, "log.jsonl");

            using (var log = new JsonlExportLog(path))
            {
                log.Info("package.start", @"D:\pkg\a.pkg", message: "out");
                log.Warn("entry.unsafe-path", @"D:\pkg\a.pkg", "../x.bin", "上跳被拒绝");
                log.Error("package.failed", @"D:\pkg\a.pkg", message: "InvalidDataException: boom");
            }

            var lines = File.ReadAllLines(path);
            Assert.Equal(3, lines.Length);

            using var first = JsonDocument.Parse(lines[0]);
            Assert.Equal("info", first.RootElement.GetProperty("level").GetString());
            Assert.Equal("package.start", first.RootElement.GetProperty("event").GetString());
            Assert.Equal(@"D:\pkg\a.pkg", first.RootElement.GetProperty("package").GetString());

            using var second = JsonDocument.Parse(lines[1]);
            Assert.Equal("warn", second.RootElement.GetProperty("level").GetString());
            Assert.Equal("../x.bin", second.RootElement.GetProperty("entry").GetString());
            Assert.Equal("上跳被拒绝", second.RootElement.GetProperty("message").GetString());

            using var third = JsonDocument.Parse(lines[2]);
            Assert.Equal("error", third.RootElement.GetProperty("level").GetString());
        }

        [Fact]
        public void Bytes_Are_Only_Written_When_NonZero()
        {
            var path = Path.Combine(_dir, "bytes.jsonl");

            using (var log = new JsonlExportLog(path))
            {
                log.Info("entry.ok", "a.pkg", "b.bin", 12345);
                log.Info("entry.ok", "a.pkg", "empty.bin", 0);
            }

            var lines = File.ReadAllLines(path);
            using var withBytes = JsonDocument.Parse(lines[0]);
            using var withoutBytes = JsonDocument.Parse(lines[1]);

            Assert.Equal(12345, withBytes.RootElement.GetProperty("bytes").GetInt64());
            Assert.False(withoutBytes.RootElement.TryGetProperty("bytes", out _));
        }

        [Fact]
        public async Task Concurrent_Writes_Produce_WellFormed_Lines()
        {
            var path = Path.Combine(_dir, "concurrent.jsonl");

            using (var log = new JsonlExportLog(path))
            {
                await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
                {
                    for (var i = 0; i < 50; i++)
                        log.Info("entry.ok", "pkg" + worker, "entry" + i, 1024);
                })));
            }

            var lines = File.ReadAllLines(path);
            Assert.Equal(400, lines.Length);

            foreach (var line in lines)
            {
                using var doc = JsonDocument.Parse(line);
                Assert.Equal("entry.ok", doc.RootElement.GetProperty("event").GetString());
            }
        }

        [Fact]
        public void Append_Mode_Preserves_Previous_Lines()
        {
            var path = Path.Combine(_dir, "append.jsonl");

            using (var log = new JsonlExportLog(path))
                log.Info("first");

            using (var log = new JsonlExportLog(path, append: true))
                log.Info("second");

            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
