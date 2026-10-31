// 基于 notscuffed/RePKG (MIT) 二次修改：本文件为 RePKG-Enhanced 新增，不来自原版。
//
// CLI 文案：帮助全文、用法错误、进度与汇总、更新检查。

namespace RePKG.Shared.Localization;

internal static class CatalogCli
{
    private const string HelpChinese =
        "RePKG-Enhanced {0}\n" +
        "Wallpaper Engine pkg 提取工具（流式、批量、跨平台）\n" +
        "\n" +
        "用法：\n" +
        "  repkg <命令> [选项] <输入>...\n" +
        "\n" +
        "命令：\n" +
        "  extract   解包 pkg 文件或目录（默认命令）\n" +
        "  info      输出 pkg 的条目清单\n" +
        "  tex       单独转换 .tex 文件\n" +
        "\n" +
        "选项：\n" +
        "  -o, --output <目录>      输出目录（默认 ./output）\n" +
        "  -e, --onlyexts <列表>    只导出这些扩展名，逗号分隔（如 tex,png）\n" +
        "  -i, --ignoreexts <列表>  跳过这些扩展名，优先级高于 --onlyexts\n" +
        "  -s, --singledir          所有条目平铺到一个目录\n" +
        "  -r, --recursive          递归扫描子目录里的 pkg\n" +
        "  -n, --usename            用 project.json 里的 title 作为目录名\n" +
        "  -c, --copyproject        把 project.json 一起复制到输出目录\n" +
        "      --no-tex-convert     只导出原始 .tex，不转 PNG/GIF\n" +
        "      --overwrite          覆盖已存在的输出文件\n" +
        "      --no-log             不写 export-log.jsonl\n" +
        "      --manifest           每个包写一份 manifest.json 资源清单\n" +
        "      --json               info 命令以 JSON 输出\n" +
        "      --concurrency <n>    并发导出任务数（默认 1）\n" +
        "      --queue-capacity <n> 导出队列上限（默认 64）\n" +
        "      --lang <zh|en|system> 界面语言（默认跟随系统/设置）\n" +
        "      --check-update       联网检查 GitHub 上的最新版本\n" +
        "      --verbose            输出更多细节\n" +
        "  -h, --help               显示本帮助\n" +
        "      --version            显示版本号\n" +
        "\n" +
        "退出码：\n" +
        "  0   全部成功\n" +
        "  1   有失败或跳过的条目\n" +
        "  2   参数错误\n" +
        "  130 用户取消\n" +
        "\n" +
        "约定：\n" +
        "  每个 pkg 导出到 <输出根>/<pkg 名>；-n 时用 project.json 的 title。\n" +
        "  仅在你主动使用 --check-update 时联网，其余时间完全离线。\n";

    private const string HelpEnglish =
        "RePKG-Enhanced {0}\n" +
        "Wallpaper Engine pkg extractor (streaming, batched, cross-platform)\n" +
        "\n" +
        "Usage:\n" +
        "  repkg <command> [options] <input>...\n" +
        "\n" +
        "Commands:\n" +
        "  extract   Extract pkg files or directories (default command)\n" +
        "  info      Print the entry table of a pkg\n" +
        "  tex       Convert standalone .tex files\n" +
        "\n" +
        "Options:\n" +
        "  -o, --output <dir>        Output directory (default ./output)\n" +
        "  -e, --onlyexts <list>     Only extract these extensions, comma separated (tex,png)\n" +
        "  -i, --ignoreexts <list>   Skip these extensions; wins over --onlyexts\n" +
        "  -s, --singledir           Flatten every entry into one directory\n" +
        "  -r, --recursive           Scan subdirectories for pkg files\n" +
        "  -n, --usename             Use the title from project.json as the folder name\n" +
        "  -c, --copyproject         Copy project.json into the output directory\n" +
        "      --no-tex-convert      Extract raw .tex without converting to PNG/GIF\n" +
        "      --overwrite           Overwrite existing output files\n" +
        "      --no-log              Do not write export-log.jsonl\n" +
        "      --manifest            Write a manifest.json resource list per package\n" +
        "      --json                Print info output as JSON\n" +
        "      --concurrency <n>     Number of concurrent export jobs (default 1)\n" +
        "      --queue-capacity <n>  Export queue capacity (default 64)\n" +
        "      --lang <zh|en|system> UI language (default: settings/system)\n" +
        "      --check-update        Check GitHub for the latest release\n" +
        "      --verbose             Print more detail\n" +
        "  -h, --help                Show this help\n" +
        "      --version             Show the version\n" +
        "\n" +
        "Exit codes:\n" +
        "  0   everything succeeded\n" +
        "  1   some entries failed or were skipped\n" +
        "  2   invalid arguments\n" +
        "  130 canceled by the user\n" +
        "\n" +
        "Notes:\n" +
        "  Each pkg is extracted into <output root>/<pkg name>; -n uses the project.json title.\n" +
        "  The tool is offline unless you explicitly run --check-update.\n";

    internal static void Register(LocalizationCatalog catalog)
    {
        catalog
            .Add("cli.help", HelpChinese, HelpEnglish)

            // 用法与错误
            .Add("cli.error.prefix", "参数错误：{0}", "Invalid arguments: {0}")
            .Add("cli.error.hint", "使用 repkg --help 查看用法。", "Run repkg --help for usage.")
            .Add("cli.error.missingInput", "缺少输入路径。", "Missing input path.")
            .Add("cli.error.unknownOption", "未知参数：{0}", "Unknown option: {0}")
            .Add("cli.error.optionNeedsValue", "参数 {0} 需要一个值。", "Option {0} requires a value.")
            .Add("cli.error.optionPositiveInt", "参数 {0} 需要一个正整数。", "Option {0} requires a positive integer.")
            .Add("cli.error.extListEmpty", "{0} 的扩展名列表不能为空。", "The extension list for {0} cannot be empty.")
            .Add("cli.error.pathNotFound", "路径不存在：{0}", "Path does not exist: {0}")
            .Add("cli.error.notMatchingExtension", "{0} 不是 {1} 文件。", "{0} is not a {1} file.")
            .Add("cli.error.noPackages", "没有在 {0} 找到任何 *.pkg 文件。", "No *.pkg files found in {0}")
            .Add("cli.error.noTexFiles", "没有在 {0} 找到任何 *.tex 文件。", "No *.tex files found in {0}")
            .Add("cli.canceled", "已取消。", "Canceled.")

            // 运行期
            .Add("cli.extracting", "共 {0} 个 pkg → 输出根：{1}", "{0} pkg file(s) → output root: {1}")
            .Add("cli.extract.convert", "（TEX 转 PNG/GIF/MP4）", " (TEX → PNG/GIF/MP4)")
            .Add("cli.extract.noConvert", "（不转换 TEX）", " (TEX not converted)")
            .Add("cli.extract.skipped", "[跳过] {0}：{1}", "[skipped] {0}: {1}")
            .Add("cli.tex.done", "[完成] {0} → {1}，{2}", "[done] {0} → {1}, {2}")
            .Add("cli.tex.skipped", "[跳过] {0}（目标已存在）", "[skipped] {0} (target exists)")
            .Add("cli.tex.failed", "[失败] {0}：{1}", "[failed] {0}: {1}")
            .Add("cli.texSummary", "汇总：成功 {0} / 跳过 {1} / 失败 {2}", "Summary: {0} succeeded / {1} skipped / {2} failed")

            // info
            .Add("cli.info.size", "{0}（{1} 字节）", "{0} ({1} bytes)")
            .Add("cli.info.header", "头部 {0} 字节，数据区起点 {1}", "header {0} bytes, data starts at {1}")
            .Add("cli.info.bytes", "{0} 字节", "{0} bytes")
            .Add("cli.info.failed", "[失败] {0}：{1}", "[failed] {0}: {1}")
            .Add("cli.report.done", "完成", "done")
            .Add("cli.report.failed", "失败", "failed")
            .Add("cli.report.counts",
                "成功 {0} / 跳过 {1} / 失败 {2} / 取消 {3}，{4}，{5}",
                "{0} ok / {1} skipped / {2} failed / {3} canceled, {4} in {5}")
            .Add("cli.report.reason", "       原因：{0}", "       reason: {0}")
            .Add("cli.report.log", "       日志：{0}", "       log: {0}")
            .Add("cli.report.summary",
                "汇总：{0} 个包，成功 {1} / 跳过 {2} / 失败 {3} / 取消 {4}，共 {5}，用时 {6}",
                "Summary: {0} package(s), {1} ok / {2} skipped / {3} failed / {4} canceled, {5} in {6}")
            .Add("cli.report.progress", "[{0}] {1}/{2} ({3:0}%)", "[{0}] {1}/{2} ({3:0}%)")

            // 更新检查
            .Add("cli.update.checking", "正在检查 GitHub 上的最新版本…", "Checking GitHub for the latest release…")
            .Add("cli.update.upToDate", "已是最新版本（{0}）。", "Already on the latest version ({0}).")
            .Add("cli.update.available",
                "发现新版本 {0}（当前 {1}）：{2}",
                "New version {0} available (current {1}): {2}")
            .Add("cli.update.failed", "无法检查更新：{0}", "Unable to check for updates: {0}");
    }
}
