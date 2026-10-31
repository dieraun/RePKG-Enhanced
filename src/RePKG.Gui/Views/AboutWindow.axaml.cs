// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 「关于」窗口代码隐藏：只做关闭按钮，其余全部走绑定。

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace RePKG.Gui.Views
{
    public sealed partial class AboutWindow : Window
    {
        public AboutWindow() => AvaloniaXamlLoader.Load(this);

        private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
    }
}
