// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 输入解析：文件 / 目录（递归或一层子目录）→ 待处理的 pkg 或 tex 列表。
// 原版非递归模式会扫描"当前目录 + 一层子目录"，这里保持一致。

using System;
using System.Collections.Generic;
using System.IO;

namespace RePKG.Cli
{
    internal static class InputResolver
    {
        public static List<string> CollectPackages(IEnumerable<string> inputs, bool recursive)
            => Collect(inputs, ".pkg", recursive, "pkg");

        public static List<string> CollectTexFiles(IEnumerable<string> inputs, bool recursive)
            => Collect(inputs, ".tex", recursive, "tex");

        private static List<string> Collect(IEnumerable<string> inputs, string extension, bool recursive, string kind)
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var input in inputs)
            {
                if (File.Exists(input))
                {
                    if (!input.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                        throw new CliUsageException($"{input} 不是 {extension} 文件");

                    Add(input);
                    continue;
                }

                if (!Directory.Exists(input))
                    throw new CliUsageException($"路径不存在：{input}");

                if (recursive)
                {
                    foreach (var file in Directory.EnumerateFiles(input, "*" + extension, SearchOption.AllDirectories))
                        Add(file);

                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(input, "*" + extension, SearchOption.TopDirectoryOnly))
                    Add(file);

                foreach (var directory in Directory.EnumerateDirectories(input))
                {
                    foreach (var file in Directory.EnumerateFiles(directory, "*" + extension, SearchOption.TopDirectoryOnly))
                        Add(file);
                }
            }

            results.Sort(StringComparer.OrdinalIgnoreCase);

            if (results.Count == 0)
                throw new CliUsageException($"没有找到任何 *{extension} 文件（{kind}）");

            return results;

            void Add(string path)
            {
                var full = Path.GetFullPath(path);

                if (seen.Add(full))
                    results.Add(full);
            }
        }
    }
}
