// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// App 只做装配：创建主窗口 + 视图模型，并把 StorageProvider 包成对话框服务。

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RePKG.Gui.Services;
using RePKG.Gui.ViewModels;
using RePKG.Gui.Views;

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
                var window = new MainWindow();
                var dialogs = new StorageDialogService(window);
                var viewModel = new MainWindowViewModel(dialogs);

                window.DataContext = viewModel;
                window.Opened += (_, _) => viewModel.Initialize();

                desktop.MainWindow = window;
                desktop.ShutdownRequested += (_, _) => viewModel.Dispose();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
