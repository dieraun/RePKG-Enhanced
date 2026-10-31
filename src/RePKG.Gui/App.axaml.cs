// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// App 只做装配：读设置 → 决定语言 → 创建主窗口 + 视图模型，并把 StorageProvider 包成对话框服务。
// 2.0.0：语言在启动时按「命令行 > 环境变量 REPKG_LANG > settings.json > 系统 UI 语言」解析，
// 之后由视图模型的语言菜单即时切换，本类只负责把资源字典换成新语言。

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RePKG.Gui.Localization;
using RePKG.Gui.Services;
using RePKG.Gui.ViewModels;
using RePKG.Gui.Views;
using RePKG.Shared.Localization;
using RePKG.Shared.Settings;

namespace RePKG.Gui
{
    // 注意：`RePKG.Application` 是个命名空间，这里必须写全 `Avalonia.Application`
    public sealed partial class App : Avalonia.Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var settings = new SettingsStore();
                var language = LanguageResolver.Resolve(null, settings);
                var localizer = new Localizer(language: language);

                LocalizationResources.Apply(this, localizer);

                // 换语言时只刷新资源字典；视图模型自己订阅同一个事件刷新状态文案
                localizer.LanguageChanged += (_, _) => LocalizationResources.Apply(this, localizer);

                var window = new MainWindow();
                var dialogs = new StorageDialogService(window, localizer);
                var viewModel = new MainWindowViewModel(dialogs, localizer, settings);

                window.DataContext = viewModel;
                window.Opened += (_, _) => viewModel.Initialize();

                desktop.MainWindow = window;
                desktop.ShutdownRequested += (_, _) => viewModel.Dispose();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
