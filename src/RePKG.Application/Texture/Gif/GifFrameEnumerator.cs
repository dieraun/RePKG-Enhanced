// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 改动点：原版把整段动画的所有帧一次性物化为 ImageSharp 的 Image 列表（内存 = 帧数 × W × H × 4）。
//         这里改为惰性枚举：只保留「当前用到的那张」源位图，逐帧裁剪/旋转后立刻释放。

using System;
using System.Collections.Generic;
using RePKG.Core.Texture;
using SkiaSharp;

namespace RePKG.Application.Texture.Gif
{
    /// <summary>
    /// 按帧信息惰性产出 GIF 帧：返回合成好的位图与延迟（厘秒）。调用方负责释放返回的位图。
    /// </summary>
    internal sealed class GifFrameEnumerator : IDisposable
    {
        // 最近邻、无抗锯齿：GIF 是调色板位图，任何插值都会引入额外颜色。
        // 说明：SkiaSharp 3.119.4 只有 DrawImage 有 SKSamplingOptions 重载，DrawBitmap 仍走 SKPaint，
        //       因此这里保留 FilterQuality 并局部屏蔽 CS0618（迁移到 SKImage 会引入每帧复制）。
#pragma warning disable CS0618
        private static readonly SKPaint NearestPaint = new SKPaint
        {
            IsAntialias = false,
            FilterQuality = SKFilterQuality.None
        };
#pragma warning restore CS0618

        private readonly ITex _tex;
        private readonly IList<ITexFrameInfo> _frames;
        private readonly bool _singleFrame;

        private SKBitmap _image;
        private int _imageId = -1;
        private int _index = -1;
        private bool _disposed;

        public GifFrameEnumerator(ITex tex)
        {
            _tex = tex ?? throw new ArgumentNullException(nameof(tex));

            var container = tex.FrameInfoContainer;

            if (container != null && container.Frames != null && container.Frames.Count > 0)
            {
                _frames = container.Frames;
                _singleFrame = false;
                Width = container.GifWidth > 0 ? container.GifWidth : tex.FirstImage.FirstMipmap.Width;
                Height = container.GifHeight > 0 ? container.GifHeight : tex.FirstImage.FirstMipmap.Height;
            }
            else
            {
                // IsGif 但缺少帧信息：退化成一张静态帧
                _frames = null;
                _singleFrame = true;
                Width = tex.FirstImage.FirstMipmap.Width;
                Height = tex.FirstImage.FirstMipmap.Height;
            }

            if (Width <= 0 || Height <= 0)
                throw new InvalidOperationException($"GIF texture has an invalid canvas size {Width}x{Height}");
        }

        /// <summary>GIF 逻辑屏幕宽度。</summary>
        public int Width { get; }

        /// <summary>GIF 逻辑屏幕高度。</summary>
        public int Height { get; }

        /// <summary>
        /// 产出下一帧。返回 false 表示枚举结束。
        /// </summary>
        public bool TryNext(out SKBitmap frame, out int delayCentiseconds)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GifFrameEnumerator));

            _index++;

            if (_singleFrame)
            {
                if (_index > 0)
                {
                    frame = null;
                    delayCentiseconds = 0;
                    return false;
                }

                var source = GetImage(0);
                frame = SkiaPixels.CreateTransparent(source.Width, source.Height);

                using (var canvas = new SKCanvas(frame))
                    canvas.DrawBitmap(source, 0, 0, NearestPaint);

                delayCentiseconds = 0;
                return true;
            }

            if (_index >= _frames.Count)
            {
                frame = null;
                delayCentiseconds = 0;
                return false;
            }

            var info = _frames[_index];
            frame = Compose(info);
            delayCentiseconds = Math.Max(0, (int) Math.Round(info.Frametime * 100.0f));
            return true;
        }

        private SKBitmap Compose(ITexFrameInfo info)
        {
            // 与原版一致：Width 为 0 时用 HeightX 兜底，Height 为 0 时用 WidthY 兜底
            var frameWidth = info.Width != 0 ? info.Width : info.HeightX;
            var frameHeight = info.Height != 0 ? info.Height : info.WidthY;

            var x = Math.Min(info.X, info.X + frameWidth);
            var y = Math.Min(info.Y, info.Y + frameHeight);

            var sourceWidth = Math.Abs(frameWidth);
            var sourceHeight = Math.Abs(frameHeight);

            // 原版角度公式等价于 0 / ±90 度
            var angle = -(Math.Atan2(Math.Sign(frameHeight), Math.Sign(frameWidth)) - Math.PI / 4);
            var degrees = (int) Math.Round(angle * 180.0 / Math.PI);

            var swapped = degrees == 90 || degrees == -90;

            var targetWidth = (int) Math.Round(swapped ? sourceHeight : sourceWidth);
            var targetHeight = (int) Math.Round(swapped ? sourceWidth : sourceHeight);

            if (targetWidth <= 0 || targetHeight <= 0)
                throw new InvalidOperationException(
                    $"GIF frame {_index} has an invalid size {frameWidth}x{frameHeight}");

            var image = GetImage(info.ImageId);
            var sourceRect = Clamp(new SKRect(x, y, x + sourceWidth, y + sourceHeight), image.Width, image.Height);
            var frame = SkiaPixels.CreateTransparent(targetWidth, targetHeight);

            using (var canvas = new SKCanvas(frame))
            {
                canvas.Save();

                if (degrees == 90)
                {
                    canvas.Translate(sourceHeight, 0);
                    canvas.RotateDegrees(90);
                }
                else if (degrees == -90)
                {
                    canvas.Translate(0, sourceWidth);
                    canvas.RotateDegrees(-90);
                }

                canvas.DrawBitmap(image, sourceRect, new SKRect(0, 0, sourceWidth, sourceHeight), NearestPaint);
                canvas.Restore();
            }

            return frame;
        }

        /// <summary>按需装载帧引用的那张图（只保留一张，避免整段动画常驻内存）。</summary>
        private SKBitmap GetImage(int imageId)
        {
            if (_image != null && _imageId == imageId) return _image;

            var images = _tex.ImagesContainer?.Images;

            if (images == null || imageId < 0 || imageId >= images.Count)
                throw new InvalidOperationException(
                    $"GIF frame references image {imageId} but the texture has {images?.Count ?? 0} image(s)");

            var mipmap = images[imageId].FirstMipmap;
            var bitmap = SkiaPixels.Create(mipmap.Format, mipmap.Bytes, mipmap.Width, mipmap.Height);

            _image?.Dispose();
            _image = bitmap;
            _imageId = imageId;

            return _image;
        }

        private static SKRect Clamp(SKRect rect, int width, int height)
        {
            var left = Math.Max(0, Math.Min(rect.Left, width));
            var top = Math.Max(0, Math.Min(rect.Top, height));
            var right = Math.Max(left, Math.Min(rect.Right, width));
            var bottom = Math.Max(top, Math.Min(rect.Bottom, height));

            return new SKRect(left, top, right, bottom);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _image?.Dispose();
            _image = null;
        }
    }
}
