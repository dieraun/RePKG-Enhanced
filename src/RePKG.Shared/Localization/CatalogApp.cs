// 基于 notscuffed/RePKG (MIT) 二次修改：本文件为 RePKG-Enhanced 新增，不来自原版。
//
// 应用级文案：标题、关于窗口、语言菜单、更新检查、通用按钮。

namespace RePKG.Shared.Localization;

internal static class CatalogApp
{
    internal static void Register(LocalizationCatalog catalog)
    {
        catalog
            .Add("app.name", "RePKG-Enhanced", "RePKG-Enhanced")
            .Add("app.tagline", "Wallpaper Engine pkg 提取工具", "Wallpaper Engine pkg extractor")
            .Add("app.window.title", "RePKG-Enhanced — Wallpaper Engine pkg 提取", "RePKG-Enhanced — Wallpaper Engine pkg extractor")

            .Add("common.ok", "确定", "OK")
            .Add("common.cancel", "取消", "Cancel")
            .Add("common.close", "关闭", "Close")
            .Add("common.copy", "复制", "Copy")
            .Add("common.yes", "是", "Yes")
            .Add("common.no", "否", "No")

            // 语言菜单
            .Add("lang.menu", "语言", "Language")
            .Add("lang.system", "跟随系统", "Follow system")
            .Add("lang.zh", "简体中文", "简体中文")
            .Add("lang.en", "English", "English")

            // 关于窗口
            .Add("about.title", "关于 RePKG-Enhanced", "About RePKG-Enhanced")
            .Add("about.menu", "关于", "About")
            .Add("about.description",
                "基于 notscuffed/RePKG (MIT) 二次修改的 Wallpaper Engine pkg 工具：流式提取大文件、批量导出、TEX 转 PNG/GIF。纯离线运行，不上传任何数据。",
                "A Wallpaper Engine pkg tool derived from notscuffed/RePKG (MIT): streaming extraction of huge entries, batch export, TEX to PNG/GIF. Runs fully offline and uploads nothing.")
            .Add("about.version", "版本", "Version")
            .Add("about.informational", "完整版本号", "Informational version")
            .Add("about.author", "作者", "Author")
            .Add("about.repository", "GitHub 仓库", "GitHub repository")
            .Add("about.license", "许可", "License")
            .Add("about.license.value", "MIT（含原版 RePKG 的 MIT 许可）", "MIT (includes the upstream RePKG MIT license)")
            .Add("about.build", "构建", "Build")
            .Add("about.build.value", "{0} · {1}", "{0} · {1}")
            .Add("about.runtime", "运行环境", "Runtime")
            .Add("about.offline", "联网状态", "Network")
            .Add("about.offline.value", "仅在你点击「检查更新」时联网", "Connects only when you click \"Check for updates\"")
            .Add("about.reveal", "打开所在文件夹", "Show in folder")
            .Add("about.copied", "已复制到剪贴板", "Copied to clipboard")
            .Add("about.copyDiagnostics", "复制诊断信息", "Copy diagnostics")

            // 更新检查
            .Add("update.menu", "检查更新", "Check for updates")
            .Add("update.check", "检查更新", "Check for updates")
            .Add("update.checking", "正在检查更新…", "Checking for updates…")
            .Add("update.current", "当前版本 {0}", "Current version {0}")
            .Add("update.upToDate", "已是最新版本（{0}）。", "You are on the latest version ({0}).")
            .Add("update.available", "发现新版本 {0}（当前 {1}）。", "New version {0} available (you have {1}).")
            .Add("update.failed", "无法检查更新：{0}", "Unable to check for updates: {0}")
            .Add("update.openRelease", "打开下载页", "Open download page")
            .Add("update.published", "发布于 {0}", "Published {0}")
            .Add("update.disabled", "更新检查已禁用。", "Update checking is disabled.")
            .Add("update.diagnostics", "更新检查结果", "Update check result");
    }
}
