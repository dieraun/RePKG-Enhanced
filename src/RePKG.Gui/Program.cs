// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// Avalonia 入口。AOT 要求：XAML 全部编译进程序，不加载运行时 XAML。

using System;
using Avalonia;

namespace RePKG.Gui
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        // Avalonia 的设计器/预览器也用它，签名不能改
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
