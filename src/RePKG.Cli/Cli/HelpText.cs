// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 帮助文本与版本号集中在这里，避免散落在各命令里。

namespace RePKG.Cli
{
    internal static class HelpText
    {
        public const string Version = "0.4.0";

        public const string Usage = @"RePKG-Enhanced —— Wallpaper Engine pkg 提取工具（MIT）

用法：
  repkg extract <pkg 文件或目录>... [选项]
  repkg info    <pkg 文件或目录>... [--json]
  repkg tex     <.tex 文件或目录>...  [选项]

extract 选项：
  -o, --output <目录>        输出根目录（默认 output）
  -e, --onlyexts <列表>      只导出这些扩展名，逗号分隔，例如 tex,png
  -i, --ignoreexts <列表>    排除这些扩展名（优先于 --onlyexts）
  -s, --singledir            不保留 pkg 内的目录结构
  -r, --recursive            目录输入时递归查找 *.pkg
  -n, --usename              用 project.json 的 title 作为输出子目录名
  -c, --copyproject          同时写出 project.json
      --no-tex-convert       不转换 TEX，原样导出 .tex
      --overwrite            覆盖已存在的目标文件
      --no-log               不写 JSONL 导出日志
      --manifest             每个包写一份 manifest.json 资源清单
      --concurrency <n>      同时处理的 pkg 数量（默认 1）
      --queue-capacity <n>   排队上限（默认 64）

全局：
  -h, --help                 显示本帮助
      --version              显示版本

退出码：0 全成功 / 1 有失败项 / 2 参数错误 / 130 用户中断

约定：
  * 每个 pkg 导出到 <输出根目录>/<pkg 名>（-n 时用 project.json 的 title），
    这样批量提取不会互相覆盖。
  * TEX 转换需要把该条目读入内存（单条上限 250 MB）；大文件请用 --no-tex-convert 走纯流式导出。
";
    }
}
