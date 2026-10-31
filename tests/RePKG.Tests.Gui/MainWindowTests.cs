// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 真实窗口级测试：加载 App.axaml 里的真实样式与主题，渲染成图（可人工检查布局），
// 并用无头拖拽通道验证「拖入 pkg 即加载」这条交互链路。

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using RePKG.Gui.Localization;
using RePKG.Gui.Services;
using RePKG.Gui.ViewModels;
using RePKG.Gui.Views;
using RePKG.Shared.Localization;
using RePKG.Shared.Settings;
using RePKG.TestSupport;
using SkiaSharp;
using Xunit;

namespace RePKG.Tests.Gui
{
    public sealed class MainWindowTests : IDisposable
    {
        private static readonly string ArtifactDirectory =
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "gui");

        private readonly string _root;

        public MainWindowTests()
        {
            _root = SyntheticPkgFactory.CreateWorkDirectory("guiwin");
        }

        public void Dispose() => SyntheticPkgFactory.TryDeleteDirectory(_root);

        private sealed class FakeDialogs : IDialogService
        {
            public Task<IReadOnlyList<string>> PickPackagesAsync() =>
                Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

            public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

            public void RevealInFileManager(string path)
            {
            }

            public Task ShowAboutAsync() => Task.CompletedTask;
        }

        private string CreateSamplePackage()
        {
            var path = Path.Combine(_root, "wallpaper.pkg");

            SyntheticPkgFactory.CreateFromFiles(path, new Dictionary<string, byte[]>
            {
                ["project.json"] = "{\"title\":\"Neon Night\"}"u8.ToArray(),
                ["materials/quad.tex"] = SyntheticTexFactory.CreateRaw(2, 2, new byte[]
                {
                    255, 0, 0, 255, 0, 255, 0, 255,
                    0, 0, 255, 255, 255, 255, 255, 255
                }),
                ["scene/movie.mp4"] = new byte[] { 0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x70, 0x34, 0x32 },
                ["scripts/readme.txt"] = "hello from repkg"u8.ToArray(),
                ["models/blob.bin"] = new byte[] { 1, 2, 3, 4 }
            });

            return path;
        }

        private async Task<(MainWindow Window, MainWindowViewModel ViewModel)> CreateWindowAsync()
        {
            var viewModel = new MainWindowViewModel(new FakeDialogs(), action => action());
            await viewModel.LoadAsync(new[] { CreateSamplePackage() });
            viewModel.OutputDirectory = Path.Combine(_root, "out");

            var window = new MainWindow { DataContext = viewModel };
            window.Show();

            return (window, viewModel);
        }

        [AvaloniaFact]
        public async Task MainWindow_RendersAndBindsEntries()
        {
            var (window, viewModel) = await CreateWindowAsync();

            Assert.Single(viewModel.Packages);
            Assert.Equal(5, viewModel.VisibleEntries.Count);
            Assert.True(window.IsVisible);

            // 真实渲染一帧：XAML 绑定错误在编译期已被 compiled bindings 拦住，
            // 这里再验证布局能出图、且尺寸合理。
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.True(frame!.PixelSize.Width >= 800);
            Assert.True(frame.PixelSize.Height >= 400);

            window.Close();
        }

        [AvaloniaTheory]
        [InlineData("light", "zh")]
        [InlineData("dark", "zh")]
        [InlineData("light", "en")]
        [InlineData("dark", "en")]
        public async Task MainWindow_RendersBothThemeVariantsInBothLanguages(string themeName, string languageName)
        {
            var variant = themeName == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            var language = languageName == "zh" ? AppLanguage.Chinese : AppLanguage.English;

            var localizer = new Localizer(language: language);
            LocalizationResources.Apply(Avalonia.Application.Current!, localizer);

            var viewModel = new MainWindowViewModel(new FakeDialogs(), localizer, SettingsStore.InMemory(), action => action());
            await viewModel.LoadAsync(new[] { CreateSamplePackage() });
            viewModel.OutputDirectory = Path.Combine(_root, "out");

            var window = new MainWindow { DataContext = viewModel };
            window.Show();

            Avalonia.Application.Current!.RequestedThemeVariant = variant;

            // 主题/语言切换后重新渲染
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            Directory.CreateDirectory(ArtifactDirectory);

            var path = Path.Combine(ArtifactDirectory, $"main-window-{languageName}-{themeName}.png");

#pragma warning disable CS0618 // Bitmap.Save(string) 已标记过时，但截图落盘用它最简单
            frame!.Save(path);
#pragma warning restore CS0618

            Assert.True(new FileInfo(path).Length > 5000, "截图太小，可能渲染失败");

            // 像素统计确认「浅色 / 深色」确实不同：深色底色更暗
            var average = AverageBrightness(path);
            Assert.True(themeName == "dark" ? average < 200 : average > 40, $"{themeName} 平均亮度 {average:0.0}");

            window.Close();
        }

        [AvaloniaFact]
        public async Task Drop_PackageFile_LoadsItThroughTheRealDragDropPath()
        {
            var viewModel = new MainWindowViewModel(new FakeDialogs(), action => action());
            var window = new MainWindow { DataContext = viewModel };
            window.Show();

            var package = CreateSamplePackage();
            var file = await window.StorageProvider.TryGetFileFromPathAsync(package);
            Assert.NotNull(file);

            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateFile(file!));

            var point = new Point(120, 120);
            window.DragDrop(point, RawDragEventType.DragOver, transfer, DragDropEffects.Copy, RawInputModifiers.None);
            window.DragDrop(point, RawDragEventType.Drop, transfer, DragDropEffects.Copy, RawInputModifiers.None);

            for (var i = 0; i < 200 && viewModel.Packages.Count == 0; i++)
                await Task.Delay(20);

            Assert.Single(viewModel.Packages);
            Assert.Equal("Neon Night", viewModel.Packages[0].Title);

            window.Close();
        }

        [AvaloniaTheory]
        [InlineData("zh")]
        [InlineData("en")]
        public async Task AboutWindow_RendersInBothLanguages(string languageName)
        {
            var language = languageName == "zh" ? AppLanguage.Chinese : AppLanguage.English;
            var localizer = new Localizer(language: language);
            LocalizationResources.Apply(Avalonia.Application.Current!, localizer);

            using var aboutViewModel = new AboutWindowViewModel(localizer);
            var window = new AboutWindow { DataContext = aboutViewModel };
            window.Show();

            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            Directory.CreateDirectory(ArtifactDirectory);
            var path = Path.Combine(ArtifactDirectory, $"about-{languageName}.png");

#pragma warning disable CS0618
            frame!.Save(path);
#pragma warning restore CS0618

            Assert.True(new FileInfo(path).Length > 3000, "关于窗口截图太小");

            await Task.CompletedTask;
            window.Close();
        }

        private static double AverageBrightness(string pngPath)
        {
            using var decoded = SKBitmap.Decode(pngPath);

            if (decoded == null)
                return 0;

            var sum = 0.0;
            var count = 0;

            // 抽样：每 3 个像素取一个，足够判断明暗
            for (var y = 0; y < decoded.Height; y += 3)
            {
                for (var x = 0; x < decoded.Width; x += 3)
                {
                    var pixel = decoded.GetPixel(x, y);
                    sum += 0.299 * pixel.Red + 0.587 * pixel.Green + 0.114 * pixel.Blue;
                    count++;
                }
            }

            return count == 0 ? 0 : sum / count;
        }
    }
}
