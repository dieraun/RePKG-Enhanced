// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 改动点：原版在 Extract.cs 里直接 File.ReadAllBytes + ImageSharp 转码并写盘；
//         这里把 TEX 转换收敛为一个 IEntryConverter，由导出队列统一调度与原子写入。

using System;
using System.IO;
using System.Threading;
using RePKG.Application.Package;
using RePKG.Application.Texture;
using RePKG.Core.Package;
using RePKG.Core.Texture;
using RePKG.Shared.IO;

namespace RePKG.Shared.Export
{
    /// <summary>
    /// 把 pkg 里的 .tex 条目转成 png/gif/mp4，并可选写出同名 .json 元信息。
    ///
    /// 内存说明：TEX 解码必须把 mipmap 字节读入内存（LZ4/DXT 解压后可能进一步放大，单条上限 250 MB），
    /// 这是格式本身决定的；非 TEX 条目仍然全程流式。因此"1 GB 单条目"验收只走原始导出。
    /// </summary>
    public sealed class TexEntryConverter : IEntryConverter
    {
        private const string TexExtension = ".tex";
        private const string JsonExtension = ".json";

        private readonly TexToImageConverter _imageConverter = new TexToImageConverter();
        private readonly TexJsonInfoGenerator _jsonInfoGenerator = new TexJsonInfoGenerator();

        public bool CanConvert(PackageEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            return entry.FullPath != null &&
                   entry.FullPath.EndsWith(TexExtension, StringComparison.OrdinalIgnoreCase);
        }

        public ConversionResult Convert(
            PackageFileSource source,
            PackageEntry entry,
            string targetPath,
            ExportOptions options,
            CancellationToken cancellationToken)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (targetPath == null) throw new ArgumentNullException(nameof(targetPath));
            if (options == null) throw new ArgumentNullException(nameof(options));

            cancellationToken.ThrowIfCancellationRequested();

            ITex tex;

            using (var stream = source.OpenEntry(entry))
                tex = TexReader.Default.ReadFrom(stream, leaveOpen: false);

            cancellationToken.ThrowIfCancellationRequested();

            var format = _imageConverter.GetConvertedFormat(tex);
            var extension = format.GetFileExtension();
            var imagePath = Path.ChangeExtension(targetPath, "." + extension);
            var jsonPath = Path.ChangeExtension(targetPath, JsonExtension);

            if (!options.Overwrite && File.Exists(imagePath))
                return ConversionResult.Skip;

            long bytesWritten;

            using (var writer = AtomicFileWriter.Create(imagePath))
            {
                try
                {
                    _imageConverter.WriteTo(tex, writer.Stream);
                }
                catch
                {
                    writer.Abort();
                    throw;
                }

                writer.Commit();
                bytesWritten = writer.BytesWritten;
            }

            if (options.WriteTexInfo)
                WriteJsonInfo(tex, jsonPath, options);

            return new ConversionResult(imagePath, bytesWritten);
        }

        /// <summary>转换独立的 .tex 文件（`repkg tex` 子命令用），返回主输出路径。</summary>
        public ConversionResult ConvertFile(
            string texPath,
            string targetPath,
            ExportOptions options,
            CancellationToken cancellationToken = default)
        {
            if (texPath == null) throw new ArgumentNullException(nameof(texPath));

            cancellationToken.ThrowIfCancellationRequested();

            ITex tex;

            using (var stream = new FileStream(texPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                tex = TexReader.Default.ReadFrom(stream, leaveOpen: false);

            var format = _imageConverter.GetConvertedFormat(tex);
            var imagePath = Path.ChangeExtension(targetPath, "." + format.GetFileExtension());
            var jsonPath = Path.ChangeExtension(targetPath, JsonExtension);

            if (!options.Overwrite && File.Exists(imagePath))
                return ConversionResult.Skip;

            long bytesWritten;

            using (var writer = AtomicFileWriter.Create(imagePath))
            {
                try
                {
                    _imageConverter.WriteTo(tex, writer.Stream);
                }
                catch
                {
                    writer.Abort();
                    throw;
                }

                writer.Commit();
                bytesWritten = writer.BytesWritten;
            }

            if (options.WriteTexInfo)
                WriteJsonInfo(tex, jsonPath, options);

            return new ConversionResult(imagePath, bytesWritten);
        }

        private void WriteJsonInfo(ITex tex, string jsonPath, ExportOptions options)
        {
            string json;

            try
            {
                json = _jsonInfoGenerator.GenerateInfo(tex);
            }
            catch (InvalidOperationException)
            {
                // 动画 tex 缺少帧信息时原版也不写 json；不要因此让整条条目失败
                return;
            }

            if (!options.Overwrite && File.Exists(jsonPath))
                return;

            using (var writer = AtomicFileWriter.Create(jsonPath))
            {
                try
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    writer.Stream.Write(bytes, 0, bytes.Length);
                }
                catch
                {
                    writer.Abort();
                    throw;
                }

                writer.Commit();
            }
        }
    }
}
