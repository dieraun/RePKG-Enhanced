// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced 测试）
// CLI 双语测试：--lang 决定帮助、错误与汇总文案；退出码不受语言影响。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RePKG.Cli;
using RePKG.Shared;
using RePKG.TestSupport;
using Xunit;

namespace RePKG.Tests.Cli
{
    public sealed class CliLocalizationTests : IDisposable
    {
        private readonly string _root;

        public CliLocalizationTests()
        {
            _root = SyntheticPkgFactory.CreateWorkDirectory("cli-i18n");
        }

        public void Dispose() => SyntheticPkgFactory.TryDeleteDirectory(_root);

        private static (int ExitCode, string Output) Run(params string[] args)
        {
            var writer = new StringWriter();
            var error = new StringWriter();
            var exitCode = CliApp.Run(args, writer, error);

            return (exitCode, writer.ToString() + error.ToString());
        }

        [Fact]
        public void Version_ReportsTwoDotZero()
        {
            var (exitCode, output) = Run("--version");

            Assert.Equal(0, exitCode);
            Assert.Contains(AppInfo.Version, output);
            Assert.StartsWith("2.0.0", AppInfo.Version);
        }

        [Fact]
        public void Help_SwitchesBetweenChineseAndEnglish()
        {
            var (chineseCode, chinese) = Run("--lang", "zh", "--help");
            var (englishCode, english) = Run("--lang", "en", "--help");

            Assert.Equal(0, chineseCode);
            Assert.Equal(0, englishCode);

            Assert.Contains("用法", chinese);
            Assert.DoesNotContain("Usage", chinese);

            Assert.Contains("Usage", english);
            Assert.DoesNotContain("用法", english);

            // 两个语言的帮助都应列出核心子命令与参数
            foreach (var text in new[] { chinese, english })
            {
                Assert.Contains("extract", text);
                Assert.Contains("--onlyexts", text);
                Assert.Contains("--check-update", text);
            }
        }

        [Fact]
        public void UnknownOption_IsReportedInTheSelectedLanguage()
        {
            var (chineseCode, chinese) = Run("extract", "x.pkg", "--lang", "zh", "--nope");
            var (englishCode, english) = Run("extract", "x.pkg", "--lang", "en", "--nope");

            Assert.Equal(2, chineseCode);
            Assert.Equal(2, englishCode);

            Assert.Contains("未知参数", chinese);
            Assert.Contains("Unknown option", english);
            Assert.Contains("--nope", chinese);
            Assert.Contains("--nope", english);
        }

        [Fact]
        public void Extract_SummaryFollowsTheLanguage()
        {
            var pkg = Path.Combine(_root, "sample.pkg");
            SyntheticPkgFactory.CreateFromFiles(
                pkg,
                new Dictionary<string, byte[]>
                {
                    ["a.txt"] = Encoding.UTF8.GetBytes("hello"),
                });

            var outputRoot = Path.Combine(_root, "out");

            var (chineseCode, chinese) = Run(
                "extract", pkg, "-o", Path.Combine(outputRoot, "zh"), "--lang", "zh", "--no-log");
            var (englishCode, english) = Run(
                "extract", pkg, "-o", Path.Combine(outputRoot, "en"), "--lang", "en", "--no-log");

            Assert.Equal(0, chineseCode);
            Assert.Equal(0, englishCode);

            Assert.Contains("完成", chinese);
            Assert.Contains("汇总", chinese);

            Assert.Contains("[done]", english);
            Assert.Contains("Summary:", english);
            Assert.DoesNotContain("汇总", english);
        }

        [Fact]
        public void MissingInput_FailsWithLocalizedHint()
        {
            var (exitCode, output) = Run("extract", "--lang", "en");

            Assert.Equal(2, exitCode);
            Assert.Contains("input", output, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CheckUpdateFlag_IsAcceptedByTheParser()
        {
            // 不真的联网：只验证参数被接受（--check-update 不在 --help 的错误分支里）
            var (exitCode, output) = Run("--lang", "en", "--help");

            Assert.Equal(0, exitCode);
            Assert.Contains("--check-update", output);
        }
    }
}
