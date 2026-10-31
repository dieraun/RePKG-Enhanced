// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 本地化内核测试：文案表完整性、占位符一致性、语言解析与运行时切换。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RePKG.Shared.Localization;
using Xunit;

namespace RePKG.Tests.Shared.Localization
{
    public sealed class LocalizationTests
    {
        private static readonly LocalizationCatalog Catalog = LocalizationCatalog.CreateDefault();

        [Fact]
        public void Catalog_HasMatchingKeysInBothLanguages()
        {
            Assert.True(Catalog.Count > 100, $"文案条目太少：{Catalog.Count}");

            var missingInEnglish = Catalog.Chinese.Keys
                .Where(key => !Catalog.English.ContainsKey(key))
                .ToArray();

            var missingInChinese = Catalog.English.Keys
                .Where(key => !Catalog.Chinese.ContainsKey(key))
                .ToArray();

            Assert.Empty(missingInEnglish);
            Assert.Empty(missingInChinese);
        }

        [Fact]
        public void Catalog_PlaceholdersMatchBetweenLanguages()
        {
            var mismatched = new List<string>();

            foreach (var pair in Catalog.Chinese)
            {
                var chinese = LocalizationFormat.PlaceholderIndexes(pair.Value);
                var english = LocalizationFormat.PlaceholderIndexes(Catalog.English[pair.Key]);

                if (!chinese.SequenceEqual(english))
                {
                    mismatched.Add($"{pair.Key}: zh=[{string.Join(",", chinese)}] en=[{string.Join(",", english)}]");
                }
            }

            Assert.Empty(mismatched);
        }

        [Fact]
        public void Catalog_ContainsTheKeysTheUiNeeds()
        {
            var required = new[]
            {
                "app.window.title",
                "about.title", "about.version", "about.author", "about.repository", "about.license",
                "update.check", "update.upToDate", "update.available", "update.failed",
                "gui.addPackage", "gui.status.ready", "gui.filter.all", "gui.kind.image",
                "cli.help", "cli.error.unknownOption", "cli.report.summary"
            };

            foreach (var key in required)
            {
                Assert.True(Catalog.Chinese.ContainsKey(key), $"缺少中文文案：{key}");
                Assert.True(Catalog.English.ContainsKey(key), $"缺少英文文案：{key}");
            }
        }

        [Fact]
        public void PlaceholderIndexes_HandlesFormatsAndOrdering()
        {
            Assert.Equal(new[] { 0, 1 }, LocalizationFormat.PlaceholderIndexes("{1} of {0}"));
            Assert.Equal(new[] { 0, 1 }, LocalizationFormat.PlaceholderIndexes("{0} and {1:0.0}s"));
            Assert.Empty(LocalizationFormat.PlaceholderIndexes("no placeholders"));
            Assert.Empty(LocalizationFormat.PlaceholderIndexes("{not-a-number}"));
        }

        [Theory]
        [InlineData("zh", AppLanguage.Chinese)]
        [InlineData("zh-CN", AppLanguage.Chinese)]
        [InlineData("zh-Hans", AppLanguage.Chinese)]
        [InlineData("chinese", AppLanguage.Chinese)]
        [InlineData("中文", AppLanguage.Chinese)]
        [InlineData("en", AppLanguage.English)]
        [InlineData("en-US", AppLanguage.English)]
        [InlineData("english", AppLanguage.English)]
        [InlineData("system", AppLanguage.System)]
        [InlineData("garbage", AppLanguage.System)]
        [InlineData(null, AppLanguage.System)]
        [InlineData("", AppLanguage.System)]
        public void FromSettingValue_ParsesKnownValues(string? value, AppLanguage expected)
        {
            Assert.Equal(expected, AppLanguages.FromSettingValue(value));
        }

        [Theory]
        [InlineData("zh-CN", AppLanguage.Chinese)]
        [InlineData("zh_TW.UTF-8", AppLanguage.Chinese)]
        [InlineData("zh-Hans", AppLanguage.Chinese)]
        [InlineData("en-US", AppLanguage.English)]
        [InlineData("de-DE", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void SystemLanguage_FromName_MapsLanguageNames(string? name, AppLanguage? expected)
        {
            // 注意：本项目开了 InvariantGlobalization，运行时不能 new CultureInfo("zh-CN")，
            // 所以系统语言探测直接读 OS（Windows API / LANG 环境变量），这里只测名字归一化。
            Assert.Equal(expected, SystemLanguage.FromName(name));
        }

        [Fact]
        public void SystemLanguage_Detect_ReturnsConcreteLanguage()
        {
            var detected = SystemLanguage.Detect();

            Assert.True(detected is AppLanguage.Chinese or AppLanguage.English);
        }

        [Fact]
        public void Resolve_SystemWithoutCulture_ReturnsConcreteLanguage()
        {
            var resolved = AppLanguages.Resolve(AppLanguage.System);

            Assert.True(resolved is AppLanguage.Chinese or AppLanguage.English);
            Assert.Equal(SystemLanguage.Detect(), resolved);
        }

        [Fact]
        public void Resolve_ExplicitChoiceWinsOverSystemCulture()
        {
            Assert.Equal(AppLanguage.Chinese, AppLanguages.Resolve(AppLanguage.Chinese, CultureInfo.InvariantCulture));
            Assert.Equal(AppLanguage.English, AppLanguages.Resolve(AppLanguage.English, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void Localizer_SwitchesLanguageAndRaisesEventOnce()
        {
            var localizer = new Localizer(language: AppLanguage.Chinese);
            var raised = 0;
            localizer.LanguageChanged += (_, _) => raised++;

            var chineseTitle = localizer.Get("app.window.title");
            localizer.SetLanguage(AppLanguage.English);
            var englishTitle = localizer.Get("app.window.title");

            Assert.NotEqual(chineseTitle, englishTitle);
            Assert.Equal(1, raised);
            Assert.True(localizer.IsChinese == false);

            // 同一语言重复设置不应再次触发
            localizer.SetLanguage(AppLanguage.English);
            Assert.Equal(1, raised);
        }

        [Fact]
        public void Localizer_FormatsPlaceholders()
        {
            var localizer = new Localizer(language: AppLanguage.English);
            var text = localizer.Format("update.upToDate", "2.0.0");

            Assert.Contains("2.0.0", text);
            Assert.DoesNotContain("{0}", text);
        }

        [Fact]
        public void Localizer_ReturnsKeyForMissingText()
        {
            var localizer = new Localizer(language: AppLanguage.English);

            Assert.Equal("does.not.exist", localizer.Get("does.not.exist"));
        }

        [Fact]
        public void Localizer_ThrowsOnDuplicateKey()
        {
            var catalog = new LocalizationCatalog().Add("k", "中", "en");

            Assert.Throws<InvalidOperationException>(() => catalog.Add("k", "中2", "en2"));
        }
    }
}
