// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 输入解析：文件 / 目录（递归或一层子目录）→ 待处理的 pkg 或 tex 列表。
// 原版非递归模式会扫描"当前目录 + 一层子目录"，这里保持一致。
// 2.0.0：错误文案走本地化表。

using System;
using System.Collections.Generic;
using System.IO;
using RePKG.Shared.Localization;

namespace RePKG.Cli
{
    internal static class InputResolver
    {
        public static List<string> CollectPackages(IEnumerable<string> inputs, bool recursive, ILocalizer localizer)
            => Collect(inputs, ".pkg", recursive, localizer);

        public static List<string> CollectTexFiles(IEnumerable<string> inputs, bool recursive, ILocalizer localizer)
            => Collect(inputs, ".tex", recursive, localizer);

        private static List<string> Collect(IEnumerable<string> inputs, string extension, bool recursive, ILocalizer localizer)
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inputList = new List<string>(inputs);

            foreach (var input in inputList)
            {
                if (File.Exists(input))
                {
                    if (!input.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                        throw new CliUsageException(localizer.Format("cli.error.notMatchingExtension", input, extension));

                    Add(input);
                    continue;
                }

                if (!Directory.Exists(input))
                    throw new CliUsageException(localizer.Format("cli.error.pathNotFound", input));

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
            {
                var where = string.Join(", ", inputList);
                var key = extension == ".pkg" ? "cli.error.noPackages" : "cli.error.noTexFiles";
                throw new CliUsageException(localizer.Format(key, where));
            }

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
