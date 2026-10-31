// 基于 notscuffed/RePKG (MIT) 二次修改 —— 重写文件（RePKG-Enhanced）
// 改动点：
//  1. 图片编码栈由 SixLabors.ImageSharp 换成 SkiaSharp（MIT，Avalonia 已依赖，AOT 实测可裁剪）。
//  2. 新增 WriteTo(ITex, Stream)：直接写目标流，不再强制把结果整份塞进 byte[]。
//  3. GIF 走自研 GifWriter + 惰性 GifFrameEnumerator，逐帧量化/写出，不物化全部帧。
//  4. GetConvertedFormat 修正为对 GIF 返回 ImageGIF（原版误报 PNG）。

using System;
using System.IO;
using System.Text;
using RePKG.Application.Texture.Gif;
using RePKG.Core.Texture;
using SkiaSharp;

namespace RePKG.Application.Texture
{
    public class TexToImageConverter
    {
        // 最近邻采样：避免裁剪路径引入插值颜色。
        // 说明：SkiaSharp 3.119.4 只有 DrawImage 有 SKSamplingOptions 重载，DrawBitmap 仍走 SKPaint，
        //       因此这里保留 FilterQuality 并局部屏蔽 CS0618。
#pragma warning disable CS0618
        private static readonly SKPaint NearestPaint = new SKPaint
        {
            IsAntialias = false,
            FilterQuality = SKFilterQuality.None
        };
#pragma warning restore CS0618

        /// <summary>
        /// 转换纹理并把结果写进 <paramref name="destination"/>，返回实际写出的格式。
        /// 全程不把整个纹理重新物化进内存（GIF 逐帧处理）。
        /// </summary>
        public MipmapFormat WriteTo(ITex tex, Stream destination)
        {
            if (tex == null) throw new ArgumentNullException(nameof(tex));
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            if (tex.IsVideoTexture)
            {
                WriteVideoTexture(tex, destination);
                return MipmapFormat.VideoMp4;
            }

            if (tex.IsGif)
            {
                WriteGif(tex, destination);
                return MipmapFormat.ImageGIF;
            }

            var mipmap = tex.FirstImage.FirstMipmap;
            var format = mipmap.Format;

            if (format.IsCompressed())
                throw new InvalidOperationException("Raw mipmap format must be uncompressed");

            if (format.IsRawFormat())
            {
                WritePng(tex, mipmap, destination);
                return MipmapFormat.ImagePNG;
            }

            // 已经是图片/视频容器格式（>= 1000 的枚举），原样写出
            destination.Write(mipmap.Bytes, 0, mipmap.Bytes.Length);
            return format;
        }

        public ImageResult ConvertToImage(ITex tex)
        {
            using (var memoryStream = new MemoryStream())
            {
                var format = WriteTo(tex, memoryStream);

                return new ImageResult
                {
                    Bytes = memoryStream.ToArray(),
                    Format = format
                };
            }
        }

        public MipmapFormat GetConvertedFormat(ITex tex)
        {
            if (tex == null) throw new ArgumentNullException(nameof(tex));

            if (tex.IsVideoTexture)
                return MipmapFormat.VideoMp4;

            if (tex.IsGif)
                return MipmapFormat.ImageGIF;

            var format = tex.FirstImage.FirstMipmap.Format;

            if (format.IsCompressed())
                throw new InvalidOperationException("Raw mipmap format must be uncompressed");

            return format.IsRawFormat() ? MipmapFormat.ImagePNG : format;
        }

        private static void WriteVideoTexture(ITex tex, Stream destination)
        {
            var mipmap = tex.FirstImage.FirstMipmap;

            if (mipmap.Bytes == null || mipmap.Bytes.Length < 12)
                throw new InvalidOperationException("Expected mp4 magic header");

            var magic = Encoding.ASCII.GetString(mipmap.Bytes, 4, 8);

            if (!magic.Equals("ftypisom", StringComparison.OrdinalIgnoreCase)
                && !magic.Equals("ftypmsnv", StringComparison.OrdinalIgnoreCase)
                && !magic.Equals("ftypmp42", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Expected mp4 magic header");

            destination.Write(mipmap.Bytes, 0, mipmap.Bytes.Length);
        }

        private static void WritePng(ITex tex, ITexMipmap mipmap, Stream destination)
        {
            using (var source = SkiaPixels.Create(mipmap.Format, mipmap.Bytes, mipmap.Width, mipmap.Height))
            {
                var targetWidth = tex.Header.ImageWidth;
                var targetHeight = tex.Header.ImageHeight;

                var needsCrop = targetWidth > 0 && targetHeight > 0
                    && (targetWidth != source.Width || targetHeight != source.Height);

                if (!needsCrop)
                {
                    EncodePng(source, destination);
                    return;
                }

                // 与原版一致：按左上角裁剪到 Header 声明的图像尺寸
                var width = Math.Min(targetWidth, source.Width);
                var height = Math.Min(targetHeight, source.Height);

                using (var cropped = SkiaPixels.CreateTransparent(width, height))
                {
                    using (var canvas = new SKCanvas(cropped))
                        canvas.DrawBitmap(source, new SKRect(0, 0, width, height), new SKRect(0, 0, width, height), NearestPaint);

                    EncodePng(cropped, destination);
                }
            }
        }

        private static void EncodePng(SKBitmap bitmap, Stream destination)
        {
            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            {
                if (data == null)
                    throw new InvalidOperationException("Failed to encode texture as PNG");

                data.SaveTo(destination);
            }
        }

        private static void WriteGif(ITex tex, Stream destination)
        {
            var frameFormat = tex.FirstImage.FirstMipmap.Format;

            if (!frameFormat.IsRawFormat())
                throw new InvalidOperationException(
                    "Only raw mipmap formats are supported right now while converting gif");

            using (var frames = new GifFrameEnumerator(tex))
            using (var writer = new GifWriter(destination, frames.Width, frames.Height))
            {
                while (frames.TryNext(out var frame, out var delayCentiseconds))
                {
                    using (frame)
                    {
                        var rgba = SkiaPixels.CopyRgba(frame);
                        var quantized = ColorQuantizer.Quantize(
                            rgba, frame.Width * frame.Height, allowTransparency: true);

                        writer.WriteFrame(
                            quantized.Indices,
                            frame.Width,
                            frame.Height,
                            quantized.Palette,
                            quantized.ColorCount,
                            delayCentiseconds,
                            quantized.TransparentIndex);
                    }
                }
            }
        }
    }

    public class ImageResult
    {
        public byte[] Bytes { get; set; }
        public MipmapFormat Format { get; set; }
    }
}
