// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// IDialogService 的 Avalonia 实现：文件选择用 StorageProvider，定位用平台默认打开方式。
// 2.0.0：文件选择标题走本地化；新增「关于」对话框（延迟创建，避免启动即构造更新检查器）。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using RePKG.Gui.ViewModels;
using RePKG.Gui.Views;
using RePKG.Shared.Localization;

namespace RePKG.Gui.Services
{
    public sealed class StorageDialogService : IDialogService
    {
        private readonly Window _owner;
        private readonly ILocalizer _localizer;
        private AboutWindowViewModel? _about;

        public StorageDialogService(Window owner, ILocalizer localizer)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        public async Task<IReadOnlyList<string>> PickPackagesAsync()
        {
            var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = _localizer.Get("gui.dialog.pickPackages"),
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Wallpaper Engine pkg") { Patterns = new[] {"*.pkg"} },
                    new FilePickerFileType(_localizer.Get("gui.dialog.allFiles")) { Patterns = new[] {"*"} }
                }
            }).ConfigureAwait(true);

            var result = new List<string>(files.Count);

            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();

                if (!string.IsNullOrEmpty(path))
                    result.Add(path);
            }

            return result;
        }

        public async Task<string?> PickFolderAsync(string title)
        {
            var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            }).ConfigureAwait(true);

            if (folders.Count == 0)
                return null;

            return folders[0].TryGetLocalPath();
        }

        public async Task ShowAboutAsync()
        {
            _about ??= new AboutWindowViewModel(_localizer, OpenExternalAsync);

            var window = new AboutWindow { DataContext = _about };

            if (_owner.IsVisible)
                await window.ShowDialog(_owner).ConfigureAwait(true);
        }

        private Task OpenExternalAsync(string url)
        {
            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    return _owner.Launcher.LaunchUriAsync(uri);
            }
            catch
            {
                // 打不开浏览器不影响其他功能
            }

            return Task.CompletedTask;
        }

        public void RevealInFileManager(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // 目录用 explorer 打开；文件用 /select 定位
                    if (Directory.Exists(path))
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                    else if (File.Exists(path))
                        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start(new ProcessStartInfo("open", Directory.Exists(path) ? $"\"{path}\"" : $"-R \"{path}\"")
                    {
                        UseShellExecute = false
                    });
                }
                else
                {
                    var target = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
                    Process.Start(new ProcessStartInfo("xdg-open", $"\"{target}\"") { UseShellExecute = false });
                }
            }
            catch
            {
                // 定位失败不影响导出
            }
        }
    }
}
