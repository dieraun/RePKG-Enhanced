// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 对话框抽象：ViewModel 不直接碰 Avalonia 的 StorageProvider，便于无 UI 单测。

using System.Collections.Generic;
using System.Threading.Tasks;

namespace RePKG.Gui.Services
{
    public interface IDialogService
    {
        /// <summary>选一个或多个 pkg 文件；取消返回空列表。</summary>
        Task<IReadOnlyList<string>> PickPackagesAsync();

        /// <summary>选一个目录（用于批量递归扫描或选择输出目录）；取消返回 null。</summary>
        Task<string?> PickFolderAsync(string title);

        /// <summary>在文件管理器里定位文件/目录（失败静默）。</summary>
        void RevealInFileManager(string path);

        /// <summary>打开「关于」对话框（模态）。</summary>
        Task ShowAboutAsync();
    }
}
