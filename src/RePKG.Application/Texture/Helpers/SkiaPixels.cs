// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 改动点：原版依赖 ImageSharp 的 L8/RG88/Rgba32 像素类型；这里改成把原始 mipmap 字节
//         展开成 SkiaSharp 的 SKBitmap（RGBA8888/Unpremul），像素语义与 RG88.cs 保持一致。

using System;
using System.Runtime.InteropServices;
using RePKG.Core.Texture;
using SkiaSharp;

namespace RePKG.Application.Texture
{
    internal static class SkiaPixels
    {
        /// <summary>
        /// 把原始 mipmap 字节展开为 RGBA8888 的 <see cref="SKBitmap"/>。
        /// R8 = 灰度（复制到 RGB），RG88 = 灰度 G + 透明度 R（与原版 RG88 的隐式转换一致）。
        /// </summary>
        public static SKBitmap Create(MipmapFormat format, byte[] bytes, int width, int height)
        {
            if (width <= 0) throw new InvalidOperationException($"Mipmap width must be positive (got {width})");
            if (height <= 0) throw new InvalidOperationException($"Mipmap height must be positive (got {height})");

            var pixels = width * (long) height;
            var bytesPerPixel = format == MipmapFormat.RGBA8888 ? 4 : format == MipmapFormat.RG88 ? 2 : 1;

            if (bytes == null)
                throw new InvalidOperationException("Mipmap bytes were not read (ReadMipmapBytes = false)");

            if (bytes.LongLength < pixels * bytesPerPixel)
                throw new InvalidOperationException(
                    $"Mipmap data is truncated: {bytes.LongLength} bytes for {width}x{height} {format}");

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var bitmap = new SKBitmap(info);
            var destination = bitmap.GetPixels();

            switch (format)
            {
                case MipmapFormat.RGBA8888:
                    CopyRows(bytes, destination, width, height, bitmap.RowBytes, 4);
                    break;

                case MipmapFormat.R8:
                    ExpandGray(bytes, destination, width, height, bitmap.RowBytes);
                    break;

                case MipmapFormat.RG88:
                    ExpandGrayAlpha(bytes, destination, width, height, bitmap.RowBytes);
                    break;

                default:
                    bitmap.Dispose();
                    throw new InvalidOperationException($"Mipmap format: {format} is not a raw format");
            }

            return bitmap;
        }

        /// <summary>创建一张全透明的画布，用作逐帧合成的目标。</summary>
        public static SKBitmap CreateTransparent(int width, int height)
        {
            var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));

            using (var canvas = new SKCanvas(bitmap))
                canvas.Clear(SKColors.Transparent);

            return bitmap;
        }

        /// <summary>把位图按行拷贝成紧凑的 RGBA8888 字节（量化器输入）。</summary>
        public static byte[] CopyRgba(SKBitmap bitmap)
        {
            var width = bitmap.Width;
            var height = bitmap.Height;
            var result = new byte[width * height * 4];
            var rowBytes = width * 4;
            var source = bitmap.GetPixels();

            if (bitmap.RowBytes == rowBytes)
            {
                Marshal.Copy(source, result, 0, result.Length);
                return result;
            }

            for (var y = 0; y < height; y++)
                Marshal.Copy(IntPtr.Add(source, y * bitmap.RowBytes), result, y * rowBytes, rowBytes);

            return result;
        }

        private static void CopyRows(byte[] source, IntPtr destination, int width, int height, int rowBytes, int bytesPerPixel)
        {
            var rowLength = width * bytesPerPixel;

            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(source, y * rowLength, IntPtr.Add(destination, y * rowBytes), rowLength);
            }
        }

        private static void ExpandGray(byte[] source, IntPtr destination, int width, int height, int rowBytes)
        {
            var row = new byte[width * 4];

            for (var y = 0; y < height; y++)
            {
                var offset = y * width;

                for (var x = 0; x < width; x++)
                {
                    var value = source[offset + x];
                    var target = x * 4;
                    row[target] = value;
                    row[target + 1] = value;
                    row[target + 2] = value;
                    row[target + 3] = 255;
                }

                Marshal.Copy(row, 0, IntPtr.Add(destination, y * rowBytes), row.Length);
            }
        }

        private static void ExpandGrayAlpha(byte[] source, IntPtr destination, int width, int height, int rowBytes)
        {
            var row = new byte[width * 4];

            for (var y = 0; y < height; y++)
            {
                var offset = y * width * 2;

                for (var x = 0; x < width; x++)
                {
                    var alpha = source[offset + x * 2];
                    var gray = source[offset + x * 2 + 1];
                    var target = x * 4;
                    row[target] = gray;
                    row[target + 1] = gray;
                    row[target + 2] = gray;
                    row[target + 3] = alpha;
                }

                Marshal.Copy(row, 0, IntPtr.Add(destination, y * rowBytes), row.Length);
            }
        }
    }
}
