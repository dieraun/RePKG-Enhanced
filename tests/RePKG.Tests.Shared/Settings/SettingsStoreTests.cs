// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 设置持久化测试：往返读写、损坏文件容错、语言解析优先级、内存模式不落盘。

using System;
using System.Globalization;
using System.IO;
using RePKG.Shared.Localization;
using RePKG.Shared.Settings;
using RePKG.TestSupport;
using Xunit;

namespace RePKG.Tests.Shared.Settings
{
    public sealed class SettingsStoreTests : IDisposable
    {
        private readonly string _root;

        public SettingsStoreTests()
        {
            _root = SyntheticPkgFactory.CreateWorkDirectory("settings");
        }

        public void Dispose() => SyntheticPkgFactory.TryDeleteDirectory(_root);

        private string SettingsPath => Path.Combine(_root, "settings.json");

        [Fact]
        public void SaveThenLoad_RoundTripsLanguage()
        {
            var store = new SettingsStore(SettingsPath);

            Assert.False(store.Exists);
            Assert.True(store.Save(new AppSettings { Language = AppLanguage.English }));
            Assert.True(store.Exists);

            var loaded = new SettingsStore(SettingsPath).Load();
            Assert.Equal(AppLanguage.English, loaded.Language);
        }

        [Fact]
        public void Load_MissingFileReturnsDefaults()
        {
            var loaded = new SettingsStore(SettingsPath).Load();

            Assert.Equal(AppLanguage.System, loaded.Language);
        }

        [Fact]
        public void Load_CorruptFileReturnsDefaults()
        {
            File.WriteAllText(SettingsPath, "{ this is not json");

            var loaded = new SettingsStore(SettingsPath).Load();

            Assert.Equal(AppLanguage.System, loaded.Language);
        }

        [Fact]
        public void Save_OverwritesPreviousValue()
        {
            var store = new SettingsStore(SettingsPath);

            store.Save(new AppSettings { Language = AppLanguage.Chinese });
            store.Save(new AppSettings { Language = AppLanguage.English });

            Assert.Equal(AppLanguage.English, store.Load().Language);
        }

        [Fact]
        public void InMemory_NeverTouchesDisk()
        {
            var store = SettingsStore.InMemory();

            Assert.False(store.Save(new AppSettings { Language = AppLanguage.Chinese }));
            Assert.False(store.Exists);
            Assert.Equal(AppLanguage.System, store.Load().Language);
        }

        [Fact]
        public void LanguageResolver_PrefersCommandLineThenEnvironmentThenSettingsThenSystem()
        {
            var store = new SettingsStore(SettingsPath);
            store.Save(new AppSettings { Language = AppLanguage.Chinese });

            // InvariantGlobalization 下无法 new CultureInfo("zh-CN")，用 InvariantCulture 代表"系统给了非中文"
            var invariant = CultureInfo.InvariantCulture;

            var fromSettings = LanguageResolver.Resolve(null, store, null, invariant);
            Assert.Equal(AppLanguage.Chinese, fromSettings);

            var fromEnvironment = LanguageResolver.Resolve(null, store, "en", invariant);
            Assert.Equal(AppLanguage.English, fromEnvironment);

            var fromCommandLine = LanguageResolver.Resolve("en", store, "zh", invariant);
            Assert.Equal(AppLanguage.English, fromCommandLine);

            var fromSystem = LanguageResolver.Resolve(null, null, null, invariant);
            Assert.Equal(AppLanguage.English, fromSystem);
        }
    }
}
