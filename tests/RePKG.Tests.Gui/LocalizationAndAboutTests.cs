// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced 测试）
// 2.0.0 新增：语言切换（立即生效 + 记住选择）、关于窗口信息、更新检查（离线 stub）。

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using RePKG.Gui.ViewModels;
using RePKG.Shared;
using RePKG.Shared.Localization;
using RePKG.Shared.Settings;
using RePKG.Shared.Updates;
using RePKG.TestSupport;
using Xunit;

namespace RePKG.Tests.Gui
{
    public sealed class LocalizationAndAboutTests : IDisposable
    {
        private readonly string _root;

        public LocalizationAndAboutTests()
        {
            _root = SyntheticPkgFactory.CreateWorkDirectory("gui-l10n");
        }

        public void Dispose() => SyntheticPkgFactory.TryDeleteDirectory(_root);

        private sealed class FakeDialogs : RePKG.Gui.Services.IDialogService
        {
            public Task<IReadOnlyList<string>> PickPackagesAsync() =>
                Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

            public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

            public void RevealInFileManager(string path)
            {
            }

            public Task ShowAboutAsync() => Task.CompletedTask;
        }

        private static string CreatePackage(string directory)
        {
            var path = Path.Combine(directory, "wallpaper.pkg");
            SyntheticPkgFactory.CreateFromFiles(path, new Dictionary<string, byte[]>
            {
                ["project.json"] = "{\"title\":\"Neon Night\"}"u8.ToArray(),
                ["a/one.png"] = new byte[] { 1, 2, 3 },
            });

            return path;
        }

        [AvaloniaFact]
        public async Task SetLanguage_UpdatesTextImmediatelyAndPersists()
        {
            var settingsPath = Path.Combine(_root, "settings.json");
            var store = new SettingsStore(settingsPath);
            var localizer = new Localizer(language: AppLanguage.Chinese);

            var vm = new MainWindowViewModel(new FakeDialogs(), localizer, store, action => action());
            await vm.LoadAsync(new[] { CreatePackage(_root) });

            Assert.Contains("已加载", vm.StatusText);
            Assert.Equal("图片", vm.FilterOptions[1].Name);

            vm.SetLanguageCommand.Execute("en");

            Assert.Equal(AppLanguage.English, localizer.Resolved);
            Assert.StartsWith("Loaded", vm.StatusText);
            Assert.Equal("Images", vm.FilterOptions[1].Name);
            Assert.Equal("en", vm.LanguageSettingValue);

            // 选择被写进 settings.json，下次启动还能读到
            Assert.Equal(AppLanguage.English, store.Load().Language);
        }

        [AvaloniaFact]
        public async Task SetLanguage_ToSystemFollowsDetectedSystemLanguage()
        {
            var localizer = new Localizer(language: AppLanguage.English);
            var vm = new MainWindowViewModel(new FakeDialogs(), localizer, SettingsStore.InMemory(), action => action());
            await vm.LoadAsync(new[] { CreatePackage(_root) });

            vm.SetLanguageCommand.Execute("system");

            Assert.Equal(AppLanguage.System, localizer.Setting);
            Assert.Equal(SystemLanguage.Detect(), localizer.Resolved);
            Assert.Equal("system", vm.LanguageSettingValue);
        }

        [AvaloniaFact]
        public void About_ShowsVersionAuthorRepositoryAndLicense()
        {
            var localizer = new Localizer(language: AppLanguage.Chinese);
            using var about = new AboutWindowViewModel(localizer);

            Assert.Equal("2.0.0", about.VersionValue);
            Assert.Equal("dieraun", about.AuthorValue);
            Assert.Equal("https://github.com/dieraun/RePKG-Enhanced", about.RepositoryValue);
            Assert.Contains("MIT", about.LicenseValue);
            Assert.Contains("Windows", about.BuildValue);
            Assert.False(string.IsNullOrWhiteSpace(about.InformationalValue));

            Assert.Equal("关于 RePKG-Enhanced", about.Title);
        }

        [AvaloniaFact]
        public void About_FollowsLanguageChanges()
        {
            var localizer = new Localizer(language: AppLanguage.Chinese);
            using var about = new AboutWindowViewModel(localizer);

            var chineseTitle = about.Title;
            localizer.SetLanguage(AppLanguage.English);

            Assert.NotEqual(chineseTitle, about.Title);
            Assert.Equal("About RePKG-Enhanced", about.Title);
            Assert.Equal("Check for updates", about.CheckUpdateText);
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string _json;

            public StubHandler(string json) => _json = json;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_json) });
        }

        [AvaloniaFact]
        public async Task CheckUpdate_ReportsNewerReleaseFromStub()
        {
            const string json = """
                {
                  "tag_name": "v2.1.0",
                  "html_url": "https://github.com/dieraun/RePKG-Enhanced/releases/tag/v2.1.0",
                  "body": "notes",
                  "published_at": "2026-11-02T10:00:00Z"
                }
                """;

            var localizer = new Localizer(language: AppLanguage.English);
            using var checker = UpdateChecker.CreateForTesting(new StubHandler(json));
            using var about = new AboutWindowViewModel(localizer, checker: checker);

            await about.CheckUpdateCommand.ExecuteAsync(null);

            Assert.True(about.HasUpdate);
            Assert.Contains("2.1.0", about.UpdateStatus);
            Assert.True(about.OpenReleaseCommand.CanExecute(null));
        }

        [AvaloniaFact]
        public async Task CheckUpdate_ReportsUpToDateWhenSameVersion()
        {
            const string json = """{ "tag_name": "v2.0.0", "html_url": "https://example.com" }""";

            var localizer = new Localizer(language: AppLanguage.English);
            using var checker = UpdateChecker.CreateForTesting(new StubHandler(json));
            using var about = new AboutWindowViewModel(localizer, checker: checker);

            await about.CheckUpdateCommand.ExecuteAsync(null);

            Assert.False(about.HasUpdate);
            Assert.Contains("latest", about.UpdateStatus, StringComparison.OrdinalIgnoreCase);
        }

        [AvaloniaFact]
        public async Task CheckUpdate_ReportsFailureWithoutThrowing()
        {
            var localizer = new Localizer(language: AppLanguage.English);
            using var checker = UpdateChecker.CreateForTesting(new FailingHandler());
            using var about = new AboutWindowViewModel(localizer, checker: checker);

            await about.CheckUpdateCommand.ExecuteAsync(null);

            Assert.False(about.HasUpdate);
            Assert.False(string.IsNullOrWhiteSpace(about.UpdateStatus));
        }

        private sealed class FailingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new HttpRequestException("no network");
        }
    }
}
