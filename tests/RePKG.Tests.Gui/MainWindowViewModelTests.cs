// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 主窗口视图模型的无头测试：加载 → 勾选/三态 → 筛选 → 导出 → 暂停/取消。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using RePKG.Gui.Services;
using RePKG.Gui.ViewModels;
using RePKG.Shared.Localization;
using RePKG.Shared.Settings;
using RePKG.TestSupport;
using Xunit;

namespace RePKG.Tests.Gui
{
    public sealed class MainWindowViewModelTests : IDisposable
    {
        private readonly string _root;

        public MainWindowViewModelTests()
        {
            _root = SyntheticPkgFactory.CreateWorkDirectory("gui");
        }

        public void Dispose() => SyntheticPkgFactory.TryDeleteDirectory(_root);

        /// <summary>测试用对话框：不弹窗，直接返回预置结果。</summary>
        private sealed class FakeDialogs : IDialogService
        {
            public IReadOnlyList<string> Packages { get; set; } = Array.Empty<string>();

            public string? Folder { get; set; }

            public List<string> Revealed { get; } = new();

            public Task<IReadOnlyList<string>> PickPackagesAsync() => Task.FromResult(Packages);

            public Task<string?> PickFolderAsync(string title) => Task.FromResult(Folder);

            public void RevealInFileManager(string path) => Revealed.Add(path);

            public Task ShowAboutAsync()
            {
                AboutShown++;

                return Task.CompletedTask;
            }

            public int AboutShown { get; private set; }
        }

        private string CreatePackage(string name, params (string Path, byte[] Bytes)[] entries)
        {
            var path = Path.Combine(_root, name);
            SyntheticPkgFactory.CreateFromFiles(path, entries.ToDictionary(e => e.Path, e => e.Bytes));

            return path;
        }

        private MainWindowViewModel CreateViewModel(out FakeDialogs dialogs)
        {
            dialogs = new FakeDialogs();

            // post 同步执行，避免测试里等 Dispatcher；语言固定为中文，
            // 让断言不依赖跑测试的机器 culture（默认 System 在英文机器上会解析成 English）。
            return new MainWindowViewModel(
                dialogs,
                new Localizer(language: AppLanguage.Chinese),
                SettingsStore.InMemory(),
                action => action());
        }

        [AvaloniaFact]
        public async Task LoadAsync_PopulatesPackagesAndEntries()
        {
            var package = CreatePackage(
                "demo.pkg",
                ("project.json", "{\"title\":\"My Wallpaper\"}"u8.ToArray()),
                ("materials/quad.tex", new byte[] { 1, 2, 3 }),
                ("scene/bg.mp4", new byte[] { 4, 5, 6, 7 }));

            var vm = CreateViewModel(out _);
            await vm.LoadAsync(new[] { package });

            Assert.Single(vm.Packages);

            var loaded = vm.Packages[0];
            Assert.Equal("My Wallpaper", loaded.Title);
            Assert.Equal(3, loaded.EntryCount);
            Assert.True(vm.HasPackages);
            Assert.Same(loaded, vm.SelectedPackage);
            Assert.Equal(3, vm.VisibleEntries.Count);
        }

        [AvaloniaFact]
        public async Task LoadAsync_DeduplicatesAndSkipsCorruptPackages()
        {
            var good = CreatePackage("good.pkg", ("a.bin", new byte[] { 1 }));

            var broken = Path.Combine(_root, "broken.pkg");
            File.WriteAllBytes(broken, "not a package"u8.ToArray());

            var vm = CreateViewModel(out _);
            await vm.LoadAsync(new[] { good, good, broken });

            Assert.Single(vm.Packages);
            Assert.Contains("1", vm.StatusText);
        }

        [AvaloniaFact]
        public async Task Filter_HidesEntriesOfOtherKinds()
        {
            var package = CreatePackage(
                "kinds.pkg",
                ("img/tex.tex", new byte[] { 1 }),
                ("img/photo.png", new byte[] { 2 }),
                ("text/readme.txt", new byte[] { 3 }),
                ("video/movie.mp4", new byte[] { 4 }),
                ("blob.bin", new byte[] { 5 }));

            var vm = CreateViewModel(out _);
            await vm.LoadAsync(new[] { package });

            Assert.Equal(5, vm.VisibleEntries.Count);

            var image = vm.FilterOptions.Single(f => f.Name == "图片");
            vm.SelectedFilter = image;
            Assert.Equal(new[] { "img/photo.png", "img/tex.tex" }, vm.VisibleEntries.Select(e => e.FullPath).OrderBy(p => p));

            var text = vm.FilterOptions.Single(f => f.Name == "文本");
            vm.SelectedFilter = text;
            Assert.Equal(new[] { "text/readme.txt" }, vm.VisibleEntries.Select(e => e.FullPath));

            var video = vm.FilterOptions.Single(f => f.Name == "视频");
            vm.SelectedFilter = video;
            Assert.Equal(new[] { "video/movie.mp4" }, vm.VisibleEntries.Select(e => e.FullPath));

            var other = vm.FilterOptions.Single(f => f.Name == "其它");
            vm.SelectedFilter = other;
            Assert.Equal(new[] { "blob.bin" }, vm.VisibleEntries.Select(e => e.FullPath));

            vm.SelectedFilter = vm.FilterOptions[0];
            Assert.Equal(5, vm.VisibleEntries.Count);
        }

        [AvaloniaFact]
        public async Task Selection_PropagatesBetweenPackageAndEntries()
        {
            var package = CreatePackage(
                "sel.pkg",
                ("a.bin", new byte[] { 1 }),
                ("b.bin", new byte[] { 2 }));

            var vm = CreateViewModel(out _);
            await vm.LoadAsync(new[] { package });

            var pkg = vm.Packages[0];
            Assert.True(pkg.IsSelected);
            Assert.All(pkg.Entries, entry => Assert.True(entry.IsSelected));

            // 取消一个条目 → 包头变三态
            pkg.Entries[0].IsSelected = false;
            Assert.Null(pkg.IsSelected);

            // 再取消一个 → 包头全不选
            pkg.Entries[1].IsSelected = false;
            Assert.False(pkg.IsSelected);

            // 点包头勾上 → 全部条目被勾上
            pkg.IsSelected = true;
            Assert.All(pkg.Entries, entry => Assert.True(entry.IsSelected));
            Assert.Equal(2, pkg.SelectedCount);

            // 命令：全不选 / 全选 / 反选
            vm.SelectNoneCommand.Execute(null);
            Assert.All(pkg.Entries, entry => Assert.False(entry.IsSelected));

            vm.SelectAllCommand.Execute(null);
            Assert.All(pkg.Entries, entry => Assert.True(entry.IsSelected));

            vm.InvertSelectionCommand.Execute(null);
            Assert.All(pkg.Entries, entry => Assert.False(entry.IsSelected));
        }

        [AvaloniaFact]
        public async Task Export_WritesOnlySelectedEntriesIntoPerPackageFolder()
        {
            var package = CreatePackage(
                "export.pkg",
                ("a.bin", "aaa"u8.ToArray()),
                ("b.bin", "bbb"u8.ToArray()),
                ("c.bin", "ccc"u8.ToArray()));

            var vm = CreateViewModel(out var dialogs);
            await vm.LoadAsync(new[] { package });

            var output = Path.Combine(_root, "out");
            vm.OutputDirectory = output;
            vm.ConvertTex = false;

            var pkg = vm.Packages[0];
            pkg.Entries.Single(e => e.FullPath == "b.bin").IsSelected = false;

            await vm.ExportCommand.ExecuteAsync(null);

            var packageFolder = Path.Combine(output, "export");
            Assert.True(File.Exists(Path.Combine(packageFolder, "a.bin")));
            Assert.False(File.Exists(Path.Combine(packageFolder, "b.bin")));
            Assert.True(File.Exists(Path.Combine(packageFolder, "c.bin")));
            Assert.Equal("aaa", File.ReadAllText(Path.Combine(packageFolder, "a.bin")));
            Assert.Contains("完成", vm.StatusText);

            // 打开输出目录走对话框服务，不直接起进程
            vm.RevealOutputCommand.Execute(null);
            dialogs.Revealed.Clear();
        }

        [AvaloniaFact]
        public async Task Export_ConvertsTexAndWritesJson()
        {
            var tex = SyntheticTexFactory.CreateRaw(2, 2, new byte[]
            {
                255, 0, 0, 255, 0, 255, 0, 255,
                0, 0, 255, 255, 255, 255, 255, 255
            });

            var package = CreatePackage("tex.pkg", ("materials/quad.tex", tex));

            var vm = CreateViewModel(out _);
            await vm.LoadAsync(new[] { package });

            vm.OutputDirectory = Path.Combine(_root, "texout");
            vm.ConvertTex = true;
            vm.WriteTexInfo = true;

            await vm.ExportCommand.ExecuteAsync(null);

            var folder = Path.Combine(vm.OutputDirectory, "tex", "materials");
            Assert.True(File.Exists(Path.Combine(folder, "quad.png")));
            Assert.True(File.Exists(Path.Combine(folder, "quad.json")));

            var png = File.ReadAllBytes(Path.Combine(folder, "quad.png"));
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
        }

        [AvaloniaFact]
        public async Task Export_WritesManifestWhenEnabled()
        {
            var package = CreatePackage("manifest.pkg", ("a.bin", new byte[] { 1, 2 }));

            var vm = CreateViewModel(out _);
            await vm.LoadAsync(new[] { package });

            vm.OutputDirectory = Path.Combine(_root, "manifestout");
            vm.ConvertTex = false;
            vm.WriteManifest = true;

            await vm.ExportCommand.ExecuteAsync(null);

            var manifest = Path.Combine(vm.OutputDirectory, "manifest", "manifest.json");
            Assert.True(File.Exists(manifest));

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
            Assert.Equal(1, document.RootElement.GetProperty("entryCount").GetInt32());
        }

        [AvaloniaFact]
        public async Task Cancel_StopsExportAndLeavesNoTemporaryFiles()
        {
            // 条目足够多，才有一段时间窗可以取消（太小的包在取消前就已导出完）
            var entries = Enumerable.Range(0, 24)
                .Select(i => ("big/chunk-" + i.ToString("D2") + ".bin", new byte[512 * 1024]))
                .ToArray();

            var package = CreatePackage("big.pkg", entries);

            var vm = CreateViewModel(out _);
            await vm.LoadAsync(new[] { package });

            vm.OutputDirectory = Path.Combine(_root, "cancelout");
            vm.ConvertTex = false;
            vm.WriteManifest = false;

            var export = vm.ExportCommand.ExecuteAsync(null);

            Assert.True(vm.IsExporting);
            Assert.True(vm.CancelCommand.CanExecute(null));

            vm.CancelCommand.Execute(null);

            await export;

            Assert.False(vm.IsExporting);
            Assert.Contains("取消", vm.StatusText);

            if (Directory.Exists(vm.OutputDirectory))
            {
                Assert.Empty(Directory.GetFiles(vm.OutputDirectory, "*.tmp", SearchOption.AllDirectories));
            }
        }

        [AvaloniaFact]
        public async Task RemoveAndClear_UpdateCollections()
        {
            var first = CreatePackage("one.pkg", ("a.bin", new byte[] { 1 }));
            var second = CreatePackage("two.pkg", ("b.bin", new byte[] { 2 }));

            var vm = CreateViewModel(out _);
            await vm.LoadAsync(new[] { first, second });
            Assert.Equal(2, vm.Packages.Count);

            vm.SelectedPackage = vm.Packages[0];
            vm.RemovePackageCommand.Execute(null);
            Assert.Single(vm.Packages);
            Assert.Equal("two.pkg", vm.Packages[0].FileName);

            vm.ClearPackagesCommand.Execute(null);
            Assert.Empty(vm.Packages);
            Assert.Empty(vm.VisibleEntries);
            Assert.Null(vm.SelectedPackage);
        }

        [AvaloniaFact]
        public async Task AddFolderCommand_UsesDialogAndRecursionSetting()
        {
            var nested = Path.Combine(_root, "nested");
            Directory.CreateDirectory(nested);
            CreatePackage("top.pkg", ("a.bin", new byte[] { 1 }));

            var moved = Path.Combine(_root, "top.pkg");
            File.Move(moved, Path.Combine(nested, "deep.pkg"));

            var vm = CreateViewModel(out var dialogs);
            dialogs.Folder = _root;

            vm.Recursive = false;
            await vm.AddFolderCommand.ExecuteAsync(null);
            Assert.Empty(vm.Packages);

            vm.Recursive = true;
            await vm.AddFolderCommand.ExecuteAsync(null);
            Assert.Single(vm.Packages);
        }

        /// <summary>预览是 fire-and-forget（OnSelectedEntryChanged 里 `_ = LoadPreviewAsync(...)`），测试里轮询等待。</summary>
        private static async Task WaitForPreviewAsync(MainWindowViewModel vm)
        {
            for (var i = 0; i < 200; i++)
            {
                if (vm.PreviewImage != null || vm.PreviewText != null || vm.PreviewNote != null)
                    return;

                await Task.Delay(20);
            }

            Assert.Fail("预览在 4 秒内没有完成");
        }

        [AvaloniaFact]
        public async Task Preview_ShowsTextAndImage()
        {
            var tex = SyntheticTexFactory.CreateRaw(1, 1, new byte[] { 10, 20, 30, 255 });

            var package = CreatePackage(
                "preview.pkg",
                ("note.txt", "hello"u8.ToArray()),
                ("pic.tex", tex));

            var vm = CreateViewModel(out _);
            await vm.LoadAsync(new[] { package });

            var text = vm.VisibleEntries.Single(e => e.FullPath == "note.txt");
            vm.SelectedEntry = text;
            await WaitForPreviewAsync(vm);

            Assert.Equal("hello", vm.PreviewText);
            Assert.Null(vm.PreviewImage);

            var image = vm.VisibleEntries.Single(e => e.FullPath == "pic.tex");
            vm.SelectedEntry = image;
            await WaitForPreviewAsync(vm);

            Assert.NotNull(vm.PreviewImage);
            Assert.Null(vm.PreviewText);
        }
    }
}
