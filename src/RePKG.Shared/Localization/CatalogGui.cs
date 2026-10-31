// 基于 notscuffed/RePKG (MIT) 二次修改：本文件为 RePKG-Enhanced 新增，不来自原版。
//
// GUI 文案：主窗口控件、筛选档、状态栏、进度、预览提示。
// 键名同时用作 Avalonia 资源键（{DynamicResource gui.addPackage} 之类），见 LocalizationResources。

namespace RePKG.Shared.Localization;

internal static class CatalogGui
{
    internal static void Register(LocalizationCatalog catalog)
    {
        catalog
            // 工具栏
            .Add("gui.addPackage", "添加 pkg", "Add pkg")
            .Add("gui.addFolder", "添加目录", "Add folder")
            .Add("gui.removeSelected", "移除选中", "Remove selected")
            .Add("gui.clear", "清空", "Clear")
            .Add("gui.filter", "筛选", "Filter")
            .Add("gui.selectAll", "全选", "Select all")
            .Add("gui.selectNone", "全不选", "Select none")
            .Add("gui.invert", "反选", "Invert")

            // 筛选档
            .Add("gui.filter.all", "全部条目", "All entries")
            .Add("gui.filter.image", "图片", "Images")
            .Add("gui.filter.text", "文本", "Text")
            .Add("gui.filter.video", "视频", "Video")
            .Add("gui.filter.other", "其它", "Other")

            // 三栏
            .Add("gui.packages", "包", "Packages")
            .Add("gui.entries", "条目", "Entries")
            .Add("gui.preview", "预览", "Preview")
            .Add("gui.package.summary", "{0} 个条目 · {1}", "{0} entries · {1}")
            .Add("gui.packageCount", "{0} 个包", "{0} package(s)")
            .Add("gui.progress.package", "包 {0}/{1} · {2}", "Package {0}/{1} · {2}")
            .Add("gui.progress.job", "{0}：成功 {1} / 跳过 {2} / 失败 {3}", "{0}: {1} succeeded / {2} skipped / {3} failed")

            // 条目类型
            .Add("gui.kind.image", "图片", "image")
            .Add("gui.kind.text", "文本", "text")
            .Add("gui.kind.video", "视频", "video")
            .Add("gui.kind.other", "其它", "other")

            // 导出选项
            .Add("gui.output", "输出目录", "Output folder")
            .Add("gui.browse", "浏览…", "Browse…")
            .Add("gui.open", "打开", "Open")
            .Add("gui.recursive", "递归子目录", "Recurse subfolders")
            .Add("gui.overwrite", "覆盖已有文件", "Overwrite existing")
            .Add("gui.convertTex", "TEX 转 PNG/GIF", "TEX to PNG/GIF")
            .Add("gui.writeTexInfo", "写 .json 元信息", "Write .json metadata")
            .Add("gui.writeManifest", "写 manifest 清单", "Write manifest")
            .Add("gui.concurrency", "并发", "Concurrency")
            .Add("gui.start", "开始导出", "Start export")
            .Add("gui.pause", "暂停", "Pause")
            .Add("gui.resume", "继续", "Resume")
            .Add("gui.cancel", "取消", "Cancel")
            .Add("gui.progress.preparing", "准备中…", "Preparing…")

            // 状态栏
            .Add("gui.status.ready",
                "就绪。拖入 .pkg 文件，或点击「添加 pkg」开始。",
                "Ready. Drop a .pkg file, or click 'Add pkg' to begin.")
            .Add("gui.status.outputMissing", "输出目录不可用：{0}", "Output folder is not usable: {0}")
            .Add("gui.status.noNewPackages", "没有新的 pkg 可添加。", "No new pkg files to add.")
            .Add("gui.status.readingManifests", "正在读取 {0} 个包的清单…", "Reading {0} package manifest(s)…")
            .Add("gui.status.loaded", "已加载 {0} 个包。", "Loaded {0} package(s).")
            .Add("gui.status.loadedWithFailures", "已加载 {0} 个包，{1} 个读取失败。", "Loaded {0} package(s), {1} failed to read.")
            .Add("gui.status.readFailed", "读取失败：{0}", "Failed to read: {0}")
            .Add("gui.status.cleared", "已清空。", "Cleared.")
            .Add("gui.status.selectOutput", "请先选择输出目录。", "Choose an output folder first.")
            .Add("gui.status.exporting", "正在导出 {0} 个包…", "Exporting {0} package(s)…")
            .Add("gui.status.exportDone", "导出完成：{0} 个文件，{1}，用时 {2:0.0}s。", "Done: {0} file(s), {1}, {2:0.0}s.")
            .Add("gui.status.exportDoneWithFailures",
                "导出结束：成功 {0} / 跳过 {1} / 失败 {2}，{3}，用时 {4:0.0}s。",
                "Finished: {0} succeeded / {1} skipped / {2} failed, {3}, {4:0.0}s.")
            .Add("gui.status.exportFailedHint", "有 {0} 个条目失败，详见导出日志。", "{0} entry/entries failed — see the export log.")
            .Add("gui.status.canceled",
                "已取消：成功 {0} / 跳过 {1} / 失败 {2} / 取消 {3}（已完成的条目已保留）。",
                "Canceled: {0} succeeded / {1} skipped / {2} failed / {3} canceled (finished entries kept).")
            .Add("gui.status.canceledPlain", "已取消（已完成的条目已保留）。", "Canceled (finished entries kept).")
            .Add("gui.status.canceling", "正在取消…", "Canceling…")
            .Add("gui.status.exportFailed", "导出失败：{0}", "Export failed: {0}")
            .Add("gui.status.paused", "已暂停（在条目边界生效）。", "Paused (takes effect at entry boundaries).")
            .Add("gui.status.resumed", "已继续。", "Resumed.")
            .Add("gui.status.outputSet", "输出目录：{0}", "Output folder: {0}")
            .Add("gui.status.outputNotCreated", "输出目录尚不存在（导出后自动创建）。", "Output folder does not exist yet (created on export).")

            // 对话框
            .Add("gui.dialog.pickPackages", "选择 pkg 文件（可多选）", "Select pkg files (multiple allowed)")
            .Add("gui.dialog.pickPackageFolder", "选择包含 pkg 的目录", "Select a folder containing pkg files")
            .Add("gui.dialog.pickOutputFolder", "选择输出目录", "Select an output folder")
            .Add("gui.dialog.allFiles", "所有文件", "All files")

            // 预览
            .Add("gui.preview.entryMissing", "条目已不在包内。", "The entry is no longer in the package.")
            .Add("gui.preview.texUnsupported",
                "该 TEX 转出格式为 {0}，暂不支持内联预览。",
                "This TEX converts to {0}, which cannot be previewed inline.")
            .Add("gui.preview.empty", "选择左侧条目以预览。", "Select an entry on the left to preview.")
            .Add("gui.preview.binary", "二进制文件（{0}），无内联预览。", "Binary file ({0}), no inline preview.")
            .Add("gui.preview.truncated", "（已截断，仅显示前 64 KiB）", "(truncated to the first 64 KiB)")
            .Add("gui.preview.failed", "预览失败：{0}", "Preview failed: {0}");
    }
}
