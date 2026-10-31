// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced 测试）
// 覆盖：自研 GIF89a 编码器（LZW、色表、透明、帧延迟）与 GIF 帧合成。
// 验证方式是让 SkiaSharp 的 SKCodec（独立的第三方解码器）回读我们写出的字节——
// 自己写的编码器只用自己的解码器验证是不够的。

using System;
using System.Collections.Generic;
using System.IO;
using RePKG.Application.Texture;
using RePKG.Core.Texture;
using RePKG.TestSupport;
using SkiaSharp;
using Xunit;

namespace RePKG.Tests.Texture
{
    public class GifEncodingTests
    {
        private readonly TexToImageConverter _converter = new TexToImageConverter();

        [Fact]
        public void TwoFrames_ProduceAnimatedGifWithDelays()
        {
            // 8x4 的 spritesheet：左半红、右半绿，两帧各取一半
            var pixels = new byte[8 * 4 * 4];

            for (var y = 0; y < 4; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    var offset = (y * 8 + x) * 4;

                    if (x < 4)
                    {
                        pixels[offset] = 255;
                    }
                    else
                    {
                        pixels[offset + 1] = 255;
                    }

                    pixels[offset + 3] = 255;
                }
            }

            var bytes = SyntheticTexFactory.CreateGif(
                8, 4, pixels,
                new[]
                {
                    new SyntheticTexFactory.GifFrameSpec(0, 0.10f, 0, 0, 4, 4),
                    new SyntheticTexFactory.GifFrameSpec(0, 0.25f, 4, 0, 4, 4)
                },
                4, 4);

            var gif = Convert(ReadTex(bytes));

            using (var codec = SKCodec.Create(new MemoryStream(gif)))
            {
                Assert.NotNull(codec);
                Assert.Equal(2, codec.FrameCount);

                var first = DecodeFrame(codec, 0);
                var second = DecodeFrame(codec, 1);

                Assert.Equal(4, first.Width);
                Assert.Equal(4, first.Height);
                AssertColor(new SKColor(255, 0, 0, 255), first.GetPixel(0, 0));
                AssertColor(new SKColor(0, 255, 0, 255), second.GetPixel(0, 0));

                Assert.InRange(codec.FrameInfo[0].Duration, 90, 110);
                Assert.InRange(codec.FrameInfo[1].Duration, 240, 260);

                first.Dispose();
                second.Dispose();
            }
        }

        [Fact]
        public void TransparentPixels_SurviveRoundTrip()
        {
            // 4x1：前两个像素不透明蓝，后两个完全透明
            var pixels = new byte[]
            {
                0, 0, 255, 255,
                0, 0, 255, 255,
                0, 0, 0, 0,
                0, 0, 0, 0
            };

            var bytes = SyntheticTexFactory.CreateGif(
                4, 1, pixels,
                new[] { new SyntheticTexFactory.GifFrameSpec(0, 0.05f, 0, 0, 4, 1) },
                4, 1);

            var gif = Convert(ReadTex(bytes));

            using (var codec = SKCodec.Create(new MemoryStream(gif)))
            using (var frame = DecodeFrame(codec, 0))
            {
                AssertColor(new SKColor(0, 0, 255, 255), frame.GetPixel(0, 0));
                Assert.Equal(0, frame.GetPixel(3, 0).Alpha);
            }
        }

        [Fact]
        public void ManyColors_UseLocalColorTableOfAdequateSize()
        {
            // 64 种渐变色的 8x8 图，验证量化器不会把颜色压成单色
            var pixels = new byte[8 * 8 * 4];

            for (var i = 0; i < 64; i++)
            {
                pixels[i * 4] = (byte) (i * 4);
                pixels[i * 4 + 1] = (byte) (255 - i * 4);
                pixels[i * 4 + 2] = (byte) (i * 3);
                pixels[i * 4 + 3] = 255;
            }

            var bytes = SyntheticTexFactory.CreateGif(
                8, 8, pixels,
                new[] { new SyntheticTexFactory.GifFrameSpec(0, 0.05f, 0, 0, 8, 8) },
                8, 8);

            var gif = Convert(ReadTex(bytes));

            using (var codec = SKCodec.Create(new MemoryStream(gif)))
            using (var frame = DecodeFrame(codec, 0))
            {
                var first = frame.GetPixel(0, 0);
                var last = frame.GetPixel(7, 7);

                Assert.InRange(first.Red, 0, 4);
                Assert.InRange(last.Red, 248, 255);
                Assert.InRange(last.Green, 0, 8);
                Assert.Equal(255, first.Alpha);
                Assert.Equal(255, last.Alpha);
            }
        }

        private static ITex ReadTex(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
                return TexReader.Default.ReadFrom(stream);
        }

        private byte[] Convert(ITex tex)
        {
            using (var stream = new MemoryStream())
            {
                _converter.WriteTo(tex, stream);
                return stream.ToArray();
            }
        }

        private static SKBitmap DecodeFrame(SKCodec codec, int index)
        {
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var bitmap = new SKBitmap(info);
            var options = index == 0 ? new SKCodecOptions(0) : new SKCodecOptions(index, index - 1);

            var result = codec.GetPixels(info, bitmap.GetPixels(), bitmap.RowBytes, options);

            Assert.Equal(SKCodecResult.Success, result);
            return bitmap;
        }

        private static void AssertColor(SKColor expected, SKColor actual)
        {
            Assert.Equal(expected.Red, actual.Red);
            Assert.Equal(expected.Green, actual.Green);
            Assert.Equal(expected.Blue, actual.Blue);
            Assert.Equal(expected.Alpha, actual.Alpha);
        }
    }
}
