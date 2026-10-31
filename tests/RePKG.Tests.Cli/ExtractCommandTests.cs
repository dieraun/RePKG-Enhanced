// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced 测试）
// 阶段 4 门禁：用合成 pkg 走完整 CLI 链路（参数解析 → ExportQueue → 流式内核 → 落盘），
// 并校验"批量里混入坏包/坏条目"时其余照常完成、退出码正确。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using RePKG.Cli;
using RePKG.TestSupport;
using SkiaSharp;
using Xunit;

namespace RePKG.Tests.Cli
{
    public sealed class ExtractCommandTests : IDisposable
    {
        private readonly string _root;

        public ExtractCommandTests()
        {
            _root = SyntheticPkgFactory.CreateWorkDirectory("cli-tests");
        }

        public void Dispose() => SyntheticPkgFactory.TryDeleteDirectory(_root);

        [Fact]
        public void ExtractsEntriesAndWritesLog()
        {
            var pkg = Path.Combine(_root, "sample.pkg");
            var blob = new byte[4096];
            new Random(7).NextBytes(blob);

            SyntheticPkgFactory.CreateFromFiles(
                pkg,
                new Dictionary<string, byte[]>
                {
                    ["project.json"] = Encoding.UTF8.GetBytes("{\"title\":\"Sample\"}"),
                    ["a/readme.txt"] = Encoding.UTF8.GetBytes("hello world"),
                    ["a/b/blob.bin"] = blob
                });

            var output = Path.Combine(_root, "out");
            var result = Run("extract", pkg, "-o", output, "--no-tex-convert");

            Assert.Equal(0, result.ExitCode);

            var packageDir = Path.Combine(output, "sample");

            Assert.True(File.Exists(Path.Combine(packageDir, "a", "readme.txt")));
            Assert.Equal("hello world", File.ReadAllText(Path.Combine(packageDir, "a", "readme.txt")));
            Assert.Equal(blob, File.ReadAllBytes(Path.Combine(packageDir, "a", "b", "blob.bin")));
            Assert.True(File.Exists(Path.Combine(packageDir, "export-log.jsonl")));
        }

        [Fact]
        public void ConvertsTexEntryToPngAndJson()
        {
            var pkg = Path.Combine(_root, "tex.pkg");
            var tex = SyntheticTexFactory.CreateRaw(2, 2, new byte[]
            {
                255, 0, 0, 255, 0, 255, 0, 255,
                0, 0, 255, 255, 255, 255, 255, 255
            });

            SyntheticPkgFactory.CreateFromFiles(
                pkg,
                new Dictionary<string, byte[]> {["materials/icon.tex"] = tex});

            var output = Path.Combine(_root, "out-tex");
            var result = Run("extract", pkg, "-o", output);

            Assert.Equal(0, result.ExitCode);

            var packageDir = Path.Combine(output, "tex");
            var png = Path.Combine(packageDir, "materials", "icon.png");

            Assert.True(File.Exists(png));
            Assert.False(File.Exists(Path.Combine(packageDir, "materials", "icon.tex")));
            Assert.True(File.Exists(Path.Combine(packageDir, "materials", "icon.json")));

            var header = new byte[8];
            using (var stream = File.OpenRead(png))
                stream.ReadExactly(header);

            Assert.Equal(new byte[] {0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A}, header);
        }

        [Fact]
        public void RecursiveDirectory_AppliesExtensionFilter()
        {
            var nested = Path.Combine(_root, "wallpapers", "set", "inner");
            Directory.CreateDirectory(nested);

            SyntheticPkgFactory.CreateFromFiles(
                Path.Combine(nested, "one.pkg"),
                new Dictionary<string, byte[]>
                {
                    ["tex/a.tex"] = SyntheticTexFactory.CreateRaw(1, 1, new byte[] {1, 2, 3, 255}),
                    ["notes.txt"] = Encoding.UTF8.GetBytes("hello")
                });

            var output = Path.Combine(_root, "out-recursive");
            var result = Run("extract", Path.Combine(_root, "wallpapers"), "-r", "-o", output, "-e", "tex");

            Assert.Equal(0, result.ExitCode);

            var packageDir = Path.Combine(output, "one");

            Assert.True(File.Exists(Path.Combine(packageDir, "tex", "a.png")));
            Assert.False(File.Exists(Path.Combine(packageDir, "notes.txt")));
        }

        [Fact]
        public void CorruptPackage_FailsButOthersSucceed()
        {
            var good = Path.Combine(_root, "good.pkg");
            var bad = Path.Combine(_root, "bad.pkg");

            SyntheticPkgFactory.CreateFromFiles(
                good, new Dictionary<string, byte[]> {["ok.txt"] = Encoding.UTF8.GetBytes("ok")});

            File.WriteAllBytes(bad, Encoding.ASCII.GetBytes("not a pkg at all"));

            var output = Path.Combine(_root, "out-mixed");
            var result = Run("extract", good, bad, "-o", output, "--no-tex-convert");

            Assert.Equal(1, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(output, "good", "ok.txt")));
            Assert.Contains("失败", result.Output);
        }

        [Fact]
        public void UnsafeEntryPath_IsSkippedAndDoesNotEscape()
        {
            var pkg = Path.Combine(_root, "unsafe.pkg");

            SyntheticPkgFactory.CreateFromFiles(
                pkg,
                new Dictionary<string, byte[]>
                {
                    ["../escape.txt"] = Encoding.UTF8.GetBytes("nope"),
                    ["safe.txt"] = Encoding.UTF8.GetBytes("yes")
                });

            var output = Path.Combine(_root, "out-unsafe");
            var result = Run("extract", pkg, "-o", output, "--no-tex-convert");

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(output, "unsafe", "safe.txt")));
            Assert.False(File.Exists(Path.Combine(output, "escape.txt")));
            Assert.False(File.Exists(Path.Combine(_root, "escape.txt")));

            var log = File.ReadAllText(Path.Combine(output, "unsafe", "export-log.jsonl"));
            Assert.Contains("entry.unsafe-path", log);
        }

        [Fact]
        public void OverwriteIsOffByDefault_ThenOn()
        {
            var pkg = Path.Combine(_root, "overwrite.pkg");

            SyntheticPkgFactory.CreateFromFiles(
                pkg, new Dictionary<string, byte[]> {["file.txt"] = Encoding.UTF8.GetBytes("v1")});

            var output = Path.Combine(_root, "out-overwrite");
            var target = Path.Combine(output, "overwrite", "file.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "sentinel");

            Assert.Equal(0, Run("extract", pkg, "-o", output, "--no-tex-convert").ExitCode);
            Assert.Equal("sentinel", File.ReadAllText(target));

            Assert.Equal(0, Run("extract", pkg, "-o", output, "--no-tex-convert", "--overwrite").ExitCode);
            Assert.Equal("v1", File.ReadAllText(target));
        }

        [Fact]
        public void CopyProjectAndUseName()
        {
            var pkg = Path.Combine(_root, "named.pkg");
            var projectJson = Encoding.UTF8.GetBytes("{\"title\":\"My Wallpaper\",\"type\":\"web\"}");

            SyntheticPkgFactory.CreateFromFiles(
                pkg,
                new Dictionary<string, byte[]>
                {
                    ["project.json"] = projectJson,
                    ["index.html"] = Encoding.UTF8.GetBytes("<html></html>")
                });

            var output = Path.Combine(_root, "out-named");
            var result = Run("extract", pkg, "-o", output, "-n", "-c", "--no-tex-convert");

            Assert.Equal(0, result.ExitCode);

            var packageDir = Path.Combine(output, "My Wallpaper");

            Assert.True(File.Exists(Path.Combine(packageDir, "project.json")));
            Assert.True(File.Exists(Path.Combine(packageDir, "index.html")));
        }

        [Fact]
        public void ConvertsAnimatedTexToGif()
        {
            var pkg = Path.Combine(_root, "animated.pkg");
            var rgba = new byte[]
            {
                255, 0, 0, 255, 255, 0, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255,
                255, 0, 0, 255, 255, 0, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255
            };

            var tex = SyntheticTexFactory.CreateGif(4, 2, rgba, new[]
            {
                new SyntheticTexFactory.GifFrameSpec(0, 0.10f, 0, 0, 2, 2),
                new SyntheticTexFactory.GifFrameSpec(0, 0.20f, 2, 0, 2, 2)
            }, gifWidth: 2, gifHeight: 2);

            SyntheticPkgFactory.CreateFromFiles(
                pkg, new Dictionary<string, byte[]> {["materials/quad.tex"] = tex});

            var output = Path.Combine(_root, "out-animated");
            var result = Run("extract", pkg, "-o", output);

            Assert.Equal(0, result.ExitCode);

            var gif = Path.Combine(output, "animated", "materials", "quad.gif");
            Assert.True(File.Exists(gif));

            using var stream = File.OpenRead(gif);
            using var codec = SKCodec.Create(stream);

            Assert.NotNull(codec);
            Assert.Equal(2, codec.FrameCount);
            Assert.Equal(2, codec.Info.Width);
            Assert.Equal(2, codec.Info.Height);

            var first = codec.FrameInfo[0].Duration;
            var second = codec.FrameInfo[1].Duration;

            Assert.InRange(first, 90, 110);
            Assert.InRange(second, 190, 210);
        }

        [Fact]
        public void InfoListsEntriesAndEmitsJson()
        {
            var pkg = Path.Combine(_root, "info.pkg");

            SyntheticPkgFactory.CreateFromFiles(
                pkg,
                new Dictionary<string, byte[]>
                {
                    ["one.txt"] = new byte[10],
                    ["two.txt"] = new byte[20]
                });

            var text = Run("info", pkg);
            Assert.Equal(0, text.ExitCode);
            Assert.Contains("one.txt", text.Output);
            Assert.Contains("two.txt", text.Output);

            var json = Run("info", pkg, "--json");
            Assert.Equal(0, json.ExitCode);

            using (var document = JsonDocument.Parse(json.Output))
            {
                Assert.Equal(2, document.RootElement.GetProperty("entryCount").GetInt32());
                Assert.Equal(2, document.RootElement.GetProperty("entries").GetArrayLength());
            }
        }

        [Fact]
        public void TexCommand_ConvertsStandaloneFile()
        {
            var texPath = Path.Combine(_root, "solo.tex");
            File.WriteAllBytes(texPath, SyntheticTexFactory.CreateRaw(2, 2, new byte[]
            {
                1, 2, 3, 255, 4, 5, 6, 255,
                7, 8, 9, 255, 10, 11, 12, 255
            }));

            var output = Path.Combine(_root, "out-tex-cmd");
            var result = Run("tex", texPath, "-o", output);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(output, "solo.png")));
            Assert.True(File.Exists(Path.Combine(output, "solo.json")));
        }

        [Fact]
        public void UsageErrors_ReturnExitCodeTwo()
        {
            Assert.Equal(2, Run("extract").ExitCode);
            Assert.Equal(2, Run("extract", "--nope", "x.pkg").ExitCode);
            Assert.Equal(2, Run("extract", Path.Combine(_root, "missing.pkg")).ExitCode);
            Assert.Equal(0, Run("--help").ExitCode);
        }

        [Fact]
        public void Manifest_WritesResourceListNextToExports()
        {
            var pkg = Path.Combine(_root, "manifest.pkg");
            SyntheticPkgFactory.CreateFromFiles(pkg, new Dictionary<string, byte[]>
            {
                ["a.bin"] = new byte[] { 1, 2, 3 },
                ["sub/b.bin"] = new byte[] { 4, 5 }
            });

            var output = Path.Combine(_root, "out-manifest");
            var result = Run("extract", pkg, "-o", output, "--manifest", "--no-log");

            Assert.Equal(0, result.ExitCode);

            var manifestPath = Path.Combine(output, "manifest", "manifest.json");
            Assert.True(File.Exists(manifestPath));

            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.Equal(2, document.RootElement.GetProperty("entryCount").GetInt32());
            Assert.Equal(5, document.RootElement.GetProperty("totalBytes").GetInt64());
        }

        [Fact]
        public void Manifest_IsNotWrittenWithoutTheFlag()
        {
            var pkg = Path.Combine(_root, "nomanifest.pkg");
            SyntheticPkgFactory.CreateFromFiles(pkg, new Dictionary<string, byte[]>
            {
                ["a.bin"] = new byte[] { 1 }
            });

            var output = Path.Combine(_root, "out-nomanifest");
            var result = Run("extract", pkg, "-o", output, "--no-log");

            Assert.Equal(0, result.ExitCode);
            Assert.False(File.Exists(Path.Combine(output, "nomanifest", "manifest.json")));
        }

        private sealed class RunResult
        {
            public int ExitCode;
            public string Output = string.Empty;
        }

        private RunResult Run(params string[] args)
        {
            var writer = new StringWriter();
            var error = new StringWriter();
            var exitCode = CliApp.Run(args, writer, error);

            return new RunResult
            {
                ExitCode = exitCode,
                Output = writer.ToString() + error.ToString()
            };
        }
    }
}
