// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced 测试）
// 覆盖：TEX -> PNG 的像素映射、左上角裁剪、视频/图片容器透传，以及 GetConvertedFormat 的返回。
// 图像只依赖 SkiaSharp 自解码，不引入真实样本。

using System;
using System.IO;
using System.Text;
using RePKG.Application.Texture;
using RePKG.Core.Texture;
using SkiaSharp;
using Xunit;

namespace RePKG.Tests.Texture
{
    public class TexConversionTests
    {
        private readonly TexToImageConverter _converter = new TexToImageConverter();

        [Fact]
        public void Rgba8888_PngPreservesPixels()
        {
            var pixels = new byte[]
            {
                255, 0, 0, 255, 0, 255, 0, 255,
                0, 0, 255, 255, 255, 255, 255, 255
            };

            var png = Convert(CreateTex(MipmapFormat.RGBA8888, pixels, 2, 2));

            using (var bitmap = SKBitmap.Decode(png))
            {
                Assert.Equal(2, bitmap.Width);
                Assert.Equal(2, bitmap.Height);
                AssertColor(new SKColor(255, 0, 0, 255), bitmap.GetPixel(0, 0));
                AssertColor(new SKColor(0, 255, 0, 255), bitmap.GetPixel(1, 0));
                AssertColor(new SKColor(0, 0, 255, 255), bitmap.GetPixel(0, 1));
                AssertColor(new SKColor(255, 255, 255, 255), bitmap.GetPixel(1, 1));
            }
        }

        [Fact]
        public void R8_ExpandsToOpaqueGray()
        {
            var png = Convert(CreateTex(MipmapFormat.R8, new byte[] {10, 200}, 2, 1, TexFormat.R8));

            using (var bitmap = SKBitmap.Decode(png))
            {
                AssertColor(new SKColor(10, 10, 10, 255), bitmap.GetPixel(0, 0));
                AssertColor(new SKColor(200, 200, 200, 255), bitmap.GetPixel(1, 0));
            }
        }

        [Fact]
        public void RG88_MapsGrayToRgbAndRedToAlpha()
        {
            // 原版 RG88 的隐式转换：Rgb = G，Alpha = R
            var png = Convert(CreateTex(MipmapFormat.RG88, new byte[] {128, 200}, 1, 1, TexFormat.RG88));

            using (var bitmap = SKBitmap.Decode(png))
            {
                var color = bitmap.GetPixel(0, 0);
                Assert.InRange(color.Red, 198, 202);
                Assert.InRange(color.Green, 198, 202);
                Assert.InRange(color.Blue, 198, 202);
                Assert.InRange(color.Alpha, 126, 130);
            }
        }

        [Fact]
        public void CropsToHeaderImageSize()
        {
            // 4x4 mipmap，但 Header 声明图像是 2x2：应当从左上角裁剪
            var pixels = new byte[4 * 4 * 4];

            for (var i = 0; i < 16; i++)
            {
                pixels[i * 4] = (byte) (i * 10);
                pixels[i * 4 + 1] = 0;
                pixels[i * 4 + 2] = 0;
                pixels[i * 4 + 3] = 255;
            }

            var png = Convert(CreateTex(MipmapFormat.RGBA8888, pixels, 4, 4, imageWidth: 2, imageHeight: 2));

            using (var bitmap = SKBitmap.Decode(png))
            {
                Assert.Equal(2, bitmap.Width);
                Assert.Equal(2, bitmap.Height);
                Assert.Equal(0, bitmap.GetPixel(0, 0).Red);
                Assert.Equal(10, bitmap.GetPixel(1, 0).Red);
                Assert.Equal(40, bitmap.GetPixel(0, 1).Red);
                Assert.Equal(50, bitmap.GetPixel(1, 1).Red);
            }
        }

        [Fact]
        public void VideoTexture_PassesThroughBytes()
        {
            var bytes = Encoding.ASCII.GetBytes("????ftypisom0123456789");
            var tex = CreateTex(MipmapFormat.VideoMp4, bytes, 1, 1, texFormat: TexFormat.RGBA8888, flags: TexFlags.IsVideoTexture);

            using (var output = new MemoryStream())
            {
                var format = _converter.WriteTo(tex, output);

                Assert.Equal(MipmapFormat.VideoMp4, format);
                Assert.Equal(bytes, output.ToArray());
            }
        }

        [Fact]
        public void VideoTexture_RejectsBadMagic()
        {
            var bytes = Encoding.ASCII.GetBytes("????notamp40123456789");
            var tex = CreateTex(MipmapFormat.VideoMp4, bytes, 1, 1, flags: TexFlags.IsVideoTexture);

            using (var output = new MemoryStream())
                Assert.Throws<InvalidOperationException>(() => _converter.WriteTo(tex, output));
        }

        [Fact]
        public void ImageContainerFormat_PassesThrough()
        {
            var bytes = new byte[] {1, 2, 3, 4, 5};
            var tex = CreateTex(MipmapFormat.ImagePNG, bytes, 1, 1);

            using (var output = new MemoryStream())
            {
                var format = _converter.WriteTo(tex, output);

                Assert.Equal(MipmapFormat.ImagePNG, format);
                Assert.Equal(bytes, output.ToArray());
            }
        }

        [Fact]
        public void CompressedMipmap_IsRejected()
        {
            var tex = CreateTex(MipmapFormat.CompressedDXT5, new byte[16], 4, 4);

            using (var output = new MemoryStream())
                Assert.Throws<InvalidOperationException>(() => _converter.WriteTo(tex, output));
        }

        [Theory]
        [InlineData(MipmapFormat.RGBA8888, MipmapFormat.ImagePNG)]
        [InlineData(MipmapFormat.R8, MipmapFormat.ImagePNG)]
        [InlineData(MipmapFormat.ImagePNG, MipmapFormat.ImagePNG)]
        [InlineData(MipmapFormat.ImageBMP, MipmapFormat.ImageBMP)]
        public void GetConvertedFormat_MapsRawToPng(MipmapFormat mipmapFormat, MipmapFormat expected)
        {
            var tex = CreateTex(mipmapFormat, new byte[16], 2, 2);

            Assert.Equal(expected, _converter.GetConvertedFormat(tex));
        }

        [Fact]
        public void GetConvertedFormat_ReportsGifAndVideo()
        {
            var gif = CreateTex(MipmapFormat.RGBA8888, new byte[16], 2, 2, flags: TexFlags.IsGif);
            var video = CreateTex(MipmapFormat.VideoMp4, new byte[16], 2, 2, flags: TexFlags.IsVideoTexture);

            Assert.Equal(MipmapFormat.ImageGIF, _converter.GetConvertedFormat(gif));
            Assert.Equal(MipmapFormat.VideoMp4, _converter.GetConvertedFormat(video));
        }

        private byte[] Convert(ITex tex)
        {
            using (var stream = new MemoryStream())
            {
                _converter.WriteTo(tex, stream);
                return stream.ToArray();
            }
        }

        private static void AssertColor(SKColor expected, SKColor actual)
        {
            Assert.Equal(expected.Red, actual.Red);
            Assert.Equal(expected.Green, actual.Green);
            Assert.Equal(expected.Blue, actual.Blue);
            Assert.Equal(expected.Alpha, actual.Alpha);
        }

        internal static Tex CreateTex(
            MipmapFormat mipmapFormat,
            byte[] pixels,
            int mipmapWidth,
            int mipmapHeight,
            TexFormat texFormat = TexFormat.RGBA8888,
            TexFlags flags = TexFlags.None,
            int imageWidth = -1,
            int imageHeight = -1)
        {
            var image = new TexImage();

            image.Mipmaps.Add(new TexMipmap
            {
                Width = mipmapWidth,
                Height = mipmapHeight,
                Bytes = pixels,
                Format = mipmapFormat
            });

            var container = new TexImageContainer { Magic = "TEXB0001" };
            container.Images.Add(image);

            return new Tex
            {
                Magic1 = "TEXV0005",
                Magic2 = "TEXI0001",
                Header = new TexHeader
                {
                    Format = texFormat,
                    Flags = flags,
                    TextureWidth = mipmapWidth,
                    TextureHeight = mipmapHeight,
                    ImageWidth = imageWidth < 0 ? mipmapWidth : imageWidth,
                    ImageHeight = imageHeight < 0 ? mipmapHeight : imageHeight
                },
                ImagesContainer = container
            };
        }
    }
}
