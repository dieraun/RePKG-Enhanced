using System;
using System.Text;

namespace RePKG.Shared.Paths
{
    /// <summary>
    /// 把 pkg 条目名压平成"单层目录"下可用的文件名（<c>--singledir</c> 模式）。
    ///
    /// 与 <see cref="SafePath"/> 的分工：这里只做**改名**，不做安全判定；
    /// 结果仍必须交给 <see cref="SafePath.TryResolve"/> 复核。
    /// </summary>
    public static class FileNameSanitizer
    {
        private static readonly char[] Illegal = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

        /// <summary>
        /// 把路径里的目录分隔符替换成 <paramref name="separator"/>，并去掉非法字符。
        /// 空结果回退为 <c>entry</c>。
        /// </summary>
        public static string Flatten(string fullPath, char separator = '_')
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return "entry";

            var sb = new StringBuilder(fullPath.Length);

            foreach (var ch in fullPath)
            {
                if (ch == '/' || ch == '\\')
                {
                    sb.Append(separator);
                    continue;
                }

                if (Array.IndexOf(Illegal, ch) >= 0 || char.IsControl(ch))
                {
                    sb.Append(separator);
                    continue;
                }

                sb.Append(ch);
            }

            var result = sb.ToString().Trim();

            // Windows 不允许文件名以点或空格结尾
            result = result.TrimEnd('.', ' ');

            if (result.Length == 0)
                result = "entry";

            // 极长路径保护（与 SafePath 的上限口径一致）
            if (result.Length > 200)
            {
                var extension = System.IO.Path.GetExtension(result);
                var keep = 200 - extension.Length;

                if (keep < 1)
                {
                    result = result.Substring(0, 200);
                }
                else
                {
                    result = result.Substring(0, keep) + extension;
                }
            }

            return result;
        }
    }
}
