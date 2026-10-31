// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// IDialogService 的 Avalonia 实现：文件选择用 StorageProvider，定位用平台默认打开方式。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace RePKG.Gui.Services
{
    public sealed class StorageDialogService : IDialogService
    {
        private readonly Window _owner;

        public StorageDialogService(Window owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public async Task<IReadOnlyList<string>> PickPackagesAsync()
        {
            var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择 pkg 文件（可多选）",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Wallpaper Engine 包") { Patterns = new[] {"*.pkg"} },
                    new FilePickerFileType("所有文件") { Patterns = new[] {"*"} }
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
