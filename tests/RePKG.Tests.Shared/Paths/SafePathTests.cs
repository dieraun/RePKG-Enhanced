using System;
using System.IO;
using RePKG.Shared.Paths;
using Xunit;

namespace RePKG.Tests.Shared.Paths
{
    public sealed class SafePathTests : IDisposable
    {
        private readonly string _root;

        public SafePathTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "repkg-safepath-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_root);
        }

        [Theory]
        [InlineData("..\\evil.txt")]
        [InlineData("../evil.txt")]
        [InlineData("a/../../evil.txt")]
        [InlineData("a\\..\\..\\evil.txt")]
        [InlineData("..\\..\\..\\Windows\\System32\\evil.dll")]
        [InlineData("C:\\Windows\\evil.txt")]
        [InlineData("C:evil.txt")]
        [InlineData("/etc/passwd")]
        [InlineData("\\\\server\\share\\evil.txt")]
        [InlineData("//server/share/evil.txt")]
        [InlineData("a:b.txt")]
        [InlineData("dir/file.txt:stream")]
        [InlineData("CON")]
        [InlineData("con.txt")]
        [InlineData("dir/NUL.bin")]
        [InlineData("dir/COM1")]
        [InlineData("trailing.")]
        [InlineData("trailing ")]
        [InlineData("bad|name.txt")]
        [InlineData("bad?name.txt")]
        [InlineData("bad*name.txt")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(".")]
        [InlineData("a//b.txt")]
        public void Rejects_Dangerous_Or_Invalid_Paths(string relative)
        {
            var ok = SafePath.TryResolve(_root, relative, out var full, out var error);

            Assert.False(ok, $"应当拒绝：{relative}");
            Assert.Equal(string.Empty, full);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Theory]
        [InlineData("simple.txt")]
        [InlineData("dir/file.txt")]
        [InlineData("dir/sub/dir/file.png")]
        [InlineData("a b/c d.txt")]
        [InlineData("中文/预览 图.jpg")]
        [InlineData("dot.in.middle.bin")]
        public void Accepts_Normal_Relative_Paths(string relative)
        {
            var ok = SafePath.TryResolve(_root, relative, out var full, out var error);

            Assert.True(ok, error);
            Assert.StartsWith(Path.GetFullPath(_root), full, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(relative.Replace('/', Path.DirectorySeparatorChar), full.Substring(Path.GetFullPath(_root).Length + 1));
        }

        [Fact]
        public void Resolve_Throws_PathTraversalException()
        {
            var ex = Assert.Throws<PathTraversalException>(() => SafePath.Resolve(_root, "..\\escape.bin"));
            Assert.Contains("上跳", ex.Message);
        }

        [Fact]
        public void Accepted_Path_Stays_Under_Sibling_Prefix_Root()
        {
            // 经典前缀陷阱：root=C:\out，C:\out2\file 不能被判为在 C:\out 之下
            var siblingRoot = _root + "2";
            Directory.CreateDirectory(siblingRoot);

            try
            {
                Assert.False(SafePath.TryResolve(_root, "..\\" + Path.GetFileName(siblingRoot) + "\\x.txt", out _, out _));
            }
            finally
            {
                Directory.Delete(siblingRoot, recursive: true);
            }
        }

        [Fact]
        public void Resolve_Targets_Are_Under_Root_After_Normalization()
        {
            var ok = SafePath.TryResolve(_root, "a/./b/../c.txt", out _, out var error);
            Assert.False(ok, "包含 . 段应被拒绝（避免歧义）");
            Assert.Contains(".", error);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
