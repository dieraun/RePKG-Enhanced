using System;
using System.Collections.Generic;
using System.IO;

namespace RePKG.Shared.Paths
{
    /// <summary>
    /// 把 pkg 内部的相对路径安全地映射到输出根目录下的绝对路径。
    ///
    /// 威胁模型：pkg 是外部输入，条目名可以包含 <c>..\</c>、绝对路径、盘符、
    /// UNC、ADS（<c>a:b</c>）、保留设备名（<c>CON</c>）、尾部点/空格等。
    /// 任何一条落到根目录之外，就可能在用户机器上任意写文件。
    ///
    /// 规则（全部为硬拒绝，不做"清洗后凑合用"）：
    /// <list type="bullet">
    /// <item>空、纯空白、<c>.</c>/<c>..</c> 段</item>
    /// <item>以 <c>/</c>、<c>\</c> 开头（根路径）；含盘符（<c>X:</c>）</item>
    /// <item>UNC 前缀 <c>\\</c>；任何段含 <c>:</c>（ADS / 流）</item>
    /// <item>Windows 保留设备名（含扩展名形式，如 <c>CON.txt</c>）</item>
    /// <item>非法字符、尾部点或空格、控制字符</item>
    /// <item>规范化后不在根目录之下（大小写不敏感比较）</item>
    /// <item>落在符号链接/联接点指向外部的目录中</item>
    /// </list>
    /// </summary>
    public static class SafePath
    {
        // Windows 上不允许出现在文件名里的字符；另外补上 ':' 以阻断 ADS。
        private static readonly char[] InvalidChars = { '<', '>', ':', '"', '|', '?', '*', '\0' };

        private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>
        /// 尝试把 <paramref name="relativePath"/> 解析为 <paramref name="root"/> 之下的绝对路径。
        /// </summary>
        /// <param name="root">输出根目录（必须已存在或可创建）。</param>
        /// <param name="relativePath">pkg 条目内的相对路径。</param>
        /// <param name="fullPath">成功时返回规范化后的绝对路径。</param>
        /// <param name="error">失败原因（中文，可直接展示给用户）。</param>
        public static bool TryResolve(string root, string relativePath, out string fullPath, out string error)
        {
            fullPath = string.Empty;

            if (string.IsNullOrWhiteSpace(root))
            {
                error = "输出根目录为空";
                return false;
            }

            if (!TryNormalizeRelative(relativePath, out var normalized, out error))
                return false;

            var rootFull = Path.GetFullPath(root);
            var candidate = Path.GetFullPath(Path.Combine(rootFull, normalized));

            if (!IsUnderRoot(rootFull, candidate))
            {
                error = $"路径逃逸：'{relativePath}' 解析后落在输出目录之外";
                return false;
            }

            if (HasLinkEscape(rootFull, candidate, out error))
                return false;

            fullPath = candidate;
            error = string.Empty;
            return true;
        }

        /// <summary>与 <see cref="TryResolve"/> 相同，但失败时抛 <see cref="PathTraversalException"/>。</summary>
        public static string Resolve(string root, string relativePath)
        {
            if (TryResolve(root, relativePath, out var fullPath, out var error))
                return fullPath;

            throw new PathTraversalException(error);
        }

        private static bool TryNormalizeRelative(string relativePath, out string normalized, out string error)
        {
            normalized = string.Empty;

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                error = "条目路径为空";
                return false;
            }

            if (relativePath.IndexOf('\0') >= 0)
            {
                error = "条目路径包含 NUL 字符";
                return false;
            }

            // 绝对路径：Windows 盘符、Unix 根路径、UNC
            if (Path.IsPathRooted(relativePath))
            {
                error = $"绝对路径被拒绝：'{relativePath}'";
                return false;
            }

            if (relativePath.StartsWith("\\\\", StringComparison.Ordinal) ||
                relativePath.StartsWith("//", StringComparison.Ordinal))
            {
                error = $"UNC 路径被拒绝：'{relativePath}'";
                return false;
            }

            if (relativePath.Length >= 2 && relativePath[1] == ':')
            {
                error = $"盘符被拒绝：'{relativePath}'";
                return false;
            }

            var segments = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.None);
            var parts = new List<string>(segments.Length);

            foreach (var raw in segments)
            {
                if (raw.Length == 0)
                {
                    error = $"条目路径含空段：'{relativePath}'";
                    return false;
                }

                if (raw == ".")
                {
                    // 单段 "." 是"当前目录"，没有实际文件名可写
                    error = $"条目路径指向目录而非文件：'{relativePath}'";
                    return false;
                }

                if (raw == "..")
                {
                    error = $"相对路径上跳被拒绝：'{relativePath}'";
                    return false;
                }

                if (raw.IndexOfAny(InvalidChars) >= 0)
                {
                    error = $"条目路径含非法字符：'{raw}'";
                    return false;
                }

                if (raw.EndsWith(".", StringComparison.Ordinal) || raw.EndsWith(" ", StringComparison.Ordinal))
                {
                    error = $"文件名以点或空格结尾：'{raw}'";
                    return false;
                }

                var stem = raw;
                var dot = stem.IndexOf('.');
                if (dot >= 0) stem = stem.Substring(0, dot);

                if (ReservedNames.Contains(stem))
                {
                    error = $"使用了 Windows 保留设备名：'{raw}'";
                    return false;
                }

                parts.Add(raw);
            }

            if (parts.Count == 0)
            {
                error = $"条目路径无有效文件名：'{relativePath}'";
                return false;
            }

            normalized = string.Join(Path.DirectorySeparatorChar, parts);
            error = string.Empty;
            return true;
        }

        private static bool IsUnderRoot(string rootFull, string candidate)
        {
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            // 根目录本身不是合法文件目标
            if (string.Equals(candidate, rootFull, comparison))
                return false;

            var prefix = rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? rootFull
                : rootFull + Path.DirectorySeparatorChar;

            return candidate.StartsWith(prefix, comparison);
        }

        /// <summary>
        /// 逐级检查已存在的目录是否是指向根目录之外的链接。
        /// 不存在的部分无需检查：它们会被正常创建在根目录之下。
        /// </summary>
        private static bool HasLinkEscape(string rootFull, string candidate, out string error)
        {
            error = string.Empty;

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            var relative = candidate.Substring(rootFull.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var segments = relative.Split(Path.DirectorySeparatorChar);
            var current = rootFull;

            // 最后一段是文件名，只检查目录部分
            for (var i = 0; i < segments.Length - 1; i++)
            {
                current = Path.Combine(current, segments[i]);

                if (!Directory.Exists(current))
                    continue;

                try
                {
                    var info = new DirectoryInfo(current);
                    var target = info.ResolveLinkTarget(returnFinalTarget: true);

                    if (target == null)
                        continue;

                    var targetFull = Path.GetFullPath(target.FullName);

                    if (!string.Equals(targetFull, current, comparison))
                    {
                        error = $"目录 '{segments[i]}' 是指向外部的符号链接（→ {targetFull}）";
                        return true;
                    }
                }
                catch (IOException)
                {
                    // 无法解析链接时按危险处理
                    error = $"无法验证目录 '{segments[i]}' 是否为符号链接";
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    error = $"无权检查目录 '{segments[i]}' 是否为符号链接";
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>路径逃逸（穿越、绝对路径、盘符、链接外跳）时抛出。</summary>
    public sealed class PathTraversalException : Exception
    {
        public PathTraversalException(string message) : base(message) { }
    }
}
