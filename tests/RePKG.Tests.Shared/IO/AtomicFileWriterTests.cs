using System;
using System.IO;
using System.Linq;
using System.Text;
using RePKG.Shared.IO;
using Xunit;

namespace RePKG.Tests.Shared.IO
{
    public sealed class AtomicFileWriterTests : IDisposable
    {
        private readonly string _dir;

        public AtomicFileWriterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "repkg-atomic-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_dir);
        }

        [Fact]
        public void Commit_WritesTarget_And_RemovesTemp()
        {
            var target = Path.Combine(_dir, "out.bin");
            var payload = Encoding.UTF8.GetBytes("hello atomic");

            using (var writer = AtomicFileWriter.Create(target))
            {
                writer.Stream.Write(payload, 0, payload.Length);
                Assert.True(File.Exists(writer.TempPath), "提交前应当存在临时文件");
                Assert.False(File.Exists(target), "提交前不应出现目标文件");

                writer.Commit();
            }

            Assert.Equal(payload, File.ReadAllBytes(target));
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }

        [Fact]
        public void Dispose_Without_Commit_Leaves_No_Target_And_No_Temp()
        {
            var target = Path.Combine(_dir, "aborted.bin");

            using (var writer = AtomicFileWriter.Create(target))
            {
                writer.Stream.Write(new byte[4096], 0, 4096);
            }

            Assert.False(File.Exists(target));
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }

        [Fact]
        public void Abort_Keeps_Existing_Content()
        {
            var target = Path.Combine(_dir, "keep.bin");
            File.WriteAllText(target, "original");

            var writer = AtomicFileWriter.Create(target);
            writer.Stream.Write(new byte[] { 1, 2, 3 }, 0, 3);
            writer.Abort();

            Assert.Equal("original", File.ReadAllText(target));
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }

        [Fact]
        public void Commit_Replaces_Existing_File()
        {
            var target = Path.Combine(_dir, "replace.bin");
            File.WriteAllText(target, "old");

            using (var writer = AtomicFileWriter.Create(target))
            {
                var bytes = Encoding.UTF8.GetBytes("new content");
                writer.Stream.Write(bytes, 0, bytes.Length);
                writer.Commit();
            }

            Assert.Equal("new content", File.ReadAllText(target));
        }

        [Fact]
        public void Simulated_Failure_MidWrite_Leaves_Previous_File_Intact()
        {
            var target = Path.Combine(_dir, "crash.bin");
            File.WriteAllText(target, "previous");

            try
            {
                using var writer = AtomicFileWriter.Create(target);
                writer.Stream.Write(new byte[1024], 0, 1024);
                throw new InvalidOperationException("模拟解码失败");
            }
            catch (InvalidOperationException)
            {
            }

            Assert.Equal("previous", File.ReadAllText(target));
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }

        [Fact]
        public void Creates_Missing_Directories()
        {
            var target = Path.Combine(_dir, "a", "b", "c", "deep.bin");

            using (var writer = AtomicFileWriter.Create(target))
            {
                writer.Stream.WriteByte(7);
                writer.Commit();
            }

            Assert.True(File.Exists(target));
        }

        [Fact]
        public void Parallel_Writers_To_Different_Targets_Do_Not_Collide()
        {
            var targets = Enumerable.Range(0, 8).Select(i => Path.Combine(_dir, $"p{i}.bin")).ToArray();

            System.Threading.Tasks.Parallel.For(0, targets.Length, i =>
            {
                using var writer = AtomicFileWriter.Create(targets[i]);
                writer.Stream.Write(new[] { (byte) i }, 0, 1);
                writer.Commit();
            });

            for (var i = 0; i < targets.Length; i++)
                Assert.Equal(new[] { (byte) i }, File.ReadAllBytes(targets[i]));

            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }

        [Fact]
        public void Double_Commit_Throws()
        {
            var target = Path.Combine(_dir, "twice.bin");

            using var writer = AtomicFileWriter.Create(target);
            writer.Commit();

            Assert.Throws<InvalidOperationException>(() => writer.Commit());
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
