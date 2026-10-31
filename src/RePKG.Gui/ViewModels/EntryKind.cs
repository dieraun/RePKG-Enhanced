// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 条目分类：只按扩展名判定，供筛选与预览分派使用（与导出过滤无关）。

using System;
using System.IO;

namespace RePKG.Gui.ViewModels
{
    public enum EntryKind
    {
        Image,
        Text,
        Video,
        Other
    }

    public static class EntryKinds
    {
        private static readonly string[] ImageExtensions =
        {
            ".tex", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tga"
        };

        private static readonly string[] TextExtensions =
        {
            ".json", ".txt", ".html", ".htm", ".css", ".js", ".xml", ".md", ".ini", ".cfg", ".shader", ".vert", ".frag"
        };

        private static readonly string[] VideoExtensions =
        {
            ".mp4", ".webm", ".mov", ".mkv", ".avi"
        };

        public static EntryKind Classify(string fullPath)
        {
            var extension = Path.GetExtension(fullPath);

            if (Matches(extension, ImageExtensions)) return EntryKind.Image;
            if (Matches(extension, TextExtensions)) return EntryKind.Text;
            if (Matches(extension, VideoExtensions)) return EntryKind.Video;

            return EntryKind.Other;
        }

        private static bool Matches(string extension, string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
