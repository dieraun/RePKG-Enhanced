// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 「关于」窗口视图模型：版本/作者/仓库/许可/构建信息 + 手动更新检查。
//
// 离线底线（用户明确要求）：**绝不在启动时联网**。
// 只有用户点「检查更新」才会请求 GitHub 的 releases/latest 接口。

using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RePKG.Shared;
using RePKG.Shared.Localization;
using RePKG.Shared.Updates;

namespace RePKG.Gui.ViewModels
{
    public sealed partial class AboutWindowViewModel : ObservableObject, IDisposable
    {
        private readonly ILocalizer _localizer;
        private readonly Func<string, Task> _openExternal;
        private UpdateChecker? _checker;

        public AboutWindowViewModel(
            ILocalizer localizer,
            Func<string, Task>? openExternal = null,
            UpdateChecker? checker = null)
        {
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
            _openExternal = openExternal ?? (url => Task.CompletedTask);
            _checker = checker;

            _localizer.LanguageChanged += OnLanguageChanged;
        }

        public string Title => _localizer.Get("about.title");
        public string ProductName => AppInfo.ProductName;
        public string Tagline => _localizer.Get("app.tagline");
        public string Description => _localizer.Get("about.description");

        public string VersionLabel => _localizer.Get("about.version");
        public string VersionValue => AppInfo.Version;
        public string InformationalLabel => _localizer.Get("about.informational");
        public string InformationalValue => AppInfo.InformationalVersion;
        public string AuthorLabel => _localizer.Get("about.author");
        public string AuthorValue => AppInfo.Author;
        public string RepositoryLabel => _localizer.Get("about.repository");
        public string RepositoryValue => AppInfo.RepositoryUrl;
        public string LicenseLabel => _localizer.Get("about.license");
        public string LicenseValue => _localizer.Get("about.license.value");
        public string BuildLabel => _localizer.Get("about.build");
        public string BuildValue => _localizer.Format("about.build.value", AppInfo.OperatingSystem, AppInfo.Architecture);
        public string RuntimeLabel => _localizer.Get("about.runtime");
        public string RuntimeValue => AppInfo.RuntimeDescription;
        public string NetworkLabel => _localizer.Get("about.offline");
        public string NetworkValue => _localizer.Get("about.offline.value");

        public string CheckUpdateText => _localizer.Get("update.check");
        public string OpenRepositoryText => _localizer.Get("about.repository");
        public string OpenReleaseText => _localizer.Get("update.openRelease");
        public string CloseText => _localizer.Get("common.close");

        [ObservableProperty]
        private string _updateStatus = string.Empty;

        [ObservableProperty]
        private bool _isChecking;

        [ObservableProperty]
        private bool _hasUpdate;

        private string? _releaseUrl;

        private bool CanOpenRelease() => HasUpdate && !string.IsNullOrEmpty(_releaseUrl);

        partial void OnHasUpdateChanged(bool value) => OpenReleaseCommand.NotifyCanExecuteChanged();

        [RelayCommand]
        private async Task CheckUpdateAsync()
        {
            if (IsChecking)
                return;

            IsChecking = true;
            HasUpdate = false;
            UpdateStatus = _localizer.Get("update.checking");

            try
            {
                _checker ??= new UpdateChecker();
                var result = await _checker.CheckAsync(AppInfo.Version, CancellationToken.None).ConfigureAwait(true);

                if (!result.Succeeded)
                {
                    UpdateStatus = _localizer.Format("update.failed", result.Error ?? string.Empty);

                    return;
                }

                if (result.IsNewer)
                {
                    _releaseUrl = result.ReleaseUrl;
                    HasUpdate = true;
                    UpdateStatus = _localizer.Format("update.available", result.LatestVersion, AppInfo.Version);
                }
                else
                {
                    _releaseUrl = null;
                    UpdateStatus = _localizer.Format("update.upToDate", AppInfo.Version);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus = _localizer.Format("update.failed", ex.Message);
            }
            finally
            {
                IsChecking = false;
            }
        }

        [RelayCommand]
        private Task OpenRepositoryAsync() => _openExternal(AppInfo.RepositoryUrl);

        [RelayCommand(CanExecute = nameof(CanOpenRelease))]
        private Task OpenReleaseAsync() => _openExternal(_releaseUrl ?? AppInfo.LatestReleaseUrl);

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Tagline));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(VersionLabel));
            OnPropertyChanged(nameof(InformationalLabel));
            OnPropertyChanged(nameof(AuthorLabel));
            OnPropertyChanged(nameof(RepositoryLabel));
            OnPropertyChanged(nameof(LicenseLabel));
            OnPropertyChanged(nameof(LicenseValue));
            OnPropertyChanged(nameof(BuildLabel));
            OnPropertyChanged(nameof(CheckUpdateText));
            OnPropertyChanged(nameof(OpenRepositoryText));
            OnPropertyChanged(nameof(OpenReleaseText));
            OnPropertyChanged(nameof(CloseText));
            OnPropertyChanged(nameof(NetworkLabel));
            OnPropertyChanged(nameof(NetworkValue));
        }

        public void Dispose()
        {
            _localizer.LanguageChanged -= OnLanguageChanged;
            _checker?.Dispose();
            _checker = null;
        }
    }
}
