// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// Avalonia 入口。AOT 要求：XAML 全部编译进程序，不加载运行时 XAML。
//
// 原生库（libSkiaSharp / libHarfBuzzSharp / av_libglesv2）在 Windows AOT 单文件发布时
// 不是 exe 旁边的 DLL，而是内嵌在 exe 里的资源：必须在 Avalonia 初始化之前自解压并预加载，
// 否则 Skia 后端会以“找不到 libSkiaSharp”启动失败。

using System;
using Avalonia;
using RePKG.Shared.Native;

namespace RePKG.Gui
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            NativeBootstrap.EnsureLoaded();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia 的设计器/预览器也用它，签名不能改
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
