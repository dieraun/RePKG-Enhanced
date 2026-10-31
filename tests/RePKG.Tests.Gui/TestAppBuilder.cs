// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 无头 Avalonia 测试宿主：加载**真实的** App/样式/FluentTheme，用 Skia 真实渲染，
// 因此 XAML 编译错误、绑定路径错误、主题资源缺失都会在这里暴露。

using Avalonia;
using Avalonia.Headless;
using RePKG.Gui;

[assembly: AvaloniaTestApplication(typeof(RePKG.Tests.Gui.TestAppBuilder))]

namespace RePKG.Tests.Gui
{
    public static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                // UseHeadlessDrawing = false → 走真实 Skia，才能截图与解码位图
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia();
    }
}
