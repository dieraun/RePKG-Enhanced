// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 预览服务：TEX 用内核的 SkiaSharp 转换器转成 PNG 字节；文本条目最多读 64 KiB。
// 预览只在用户点选条目时发生，且每次只读一个条目 → 内存与 pkg 体积无关。
// 2.0.0：提示文案走本地化表（调用方把当前 ILocalizer 传进来）。

using System;
using System.IO;
using System.Text;
using RePKG.Application.Package;
using RePKG.Application.Texture;
using RePKG.Gui.ViewModels;
using RePKG.Shared.Localization;

namespace RePKG.Gui.Services
{
    public sealed class PreviewData
    {
        public PreviewData(byte[]? imageBytes = null, string? text = null, string? note = null)
        {
            ImageBytes = imageBytes;
            Text = text;
            Note = note;
        }

        public byte[]? ImageBytes { get; }
        public string? Text { get; }
        public string? Note { get; }
    }

    public static class PreviewService
    {
        public const int TextPreviewLimit = 64 * 1024;

        public static PreviewData Load(string packagePath, string fullPath, EntryKind kind, ILocalizer localizer)
        {
            using var source = PackageFileSource.Open(packagePath);
            var entry = source.FindEntry(fullPath);

            if (entry == null)
                return new PreviewData(note: localizer.Get("gui.preview.entryMissing"));

            using var stream = source.OpenEntry(entry);

            switch (kind)
            {
                case EntryKind.Text:
                    return new PreviewData(text: ReadText(stream, entry.Length, localizer));

                case EntryKind.Image:
                    return LoadImage(stream, entry.FullPath, localizer);

                default:
                    return new PreviewData(note: localizer.Format("gui.preview.binary", FormatBytes(entry.Length)));
            }
        }

        public static string ReadText(Stream stream, long length, ILocalizer localizer)
        {
            var take = (int) Math.Min(length, TextPreviewLimit);
            var buffer = new byte[take];
            var read = 0;

            while (read < take)
            {
                var n = stream.Read(buffer, read, take - read);

                if (n <= 0)
                    break;

                read += n;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, read);

            if (length > TextPreviewLimit)
                text += Environment.NewLine + Environment.NewLine + localizer.Get("gui.preview.truncated");

            return text;
        }

        private static PreviewData LoadImage(Stream stream, string fullPath, ILocalizer localizer)
        {
            var extension = Path.GetExtension(fullPath);

            if (!string.Equals(extension, ".tex", StringComparison.OrdinalIgnoreCase))
            {
                using var memory = new MemoryStream();
                stream.CopyTo(memory);

                return new PreviewData(imageBytes: memory.ToArray());
            }

            var tex = TexReader.Default.ReadFrom(stream, leaveOpen: true);
            var converter = new TexToImageConverter();
            var format = converter.GetConvertedFormat(tex);

            using var output = new MemoryStream();

            if (format == RePKG.Core.Texture.MipmapFormat.ImagePNG)
            {
                converter.WriteTo(tex, output);

                return new PreviewData(imageBytes: output.ToArray());
            }

            return new PreviewData(note: localizer.Format("gui.preview.texUnsupported", format));
        }

        public static string FormatBytes(long bytes)
        {
            string[] units = {"B", "KiB", "MiB", "GiB", "TiB"};
            double value = bytes;
            var unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0
                ? $"{bytes} B"
                : $"{value:0.##} {units[unit]}";
        }
    }
}
