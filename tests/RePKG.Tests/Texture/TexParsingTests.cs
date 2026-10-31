// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced 测试）
// 覆盖：TEX 解析的健壮性——短读、截断、长度越界字符串。
// 这些都是真实样本里最常见的失败模式：文件被截断、或恶意构造的计数。

using System;
using System.IO;
using System.Text;
using RePKG.Application;
using RePKG.Application.Exceptions;
using RePKG.Application.Texture;
using RePKG.Core.Texture;
using RePKG.TestSupport;
using Xunit;

namespace RePKG.Tests.Texture
{
    public class TexParsingTests
    {
        [Fact]
        public void RawTex_ReadsHeaderAndMipmapBytes()
        {
            var pixels = new byte[] {1, 2, 3, 4, 5, 6, 7, 8};
            var bytes = SyntheticTexFactory.CreateRaw(2, 1, pixels);

            var tex = ReadTex(bytes);

            Assert.Equal("TEXV0005", tex.Magic1);
            Assert.Equal("TEXI0001", tex.Magic2);
            Assert.Equal(TexFormat.RGBA8888, tex.Header.Format);
            Assert.Equal(2, tex.Header.ImageWidth);
            Assert.Equal(1, tex.Header.ImageHeight);
            Assert.False(tex.IsGif);
            Assert.NotNull(tex.FirstImage);
            Assert.Single(tex.FirstImage.Mipmaps);
            Assert.Equal(pixels, tex.FirstImage.FirstMipmap.Bytes);
            Assert.Equal(MipmapFormat.RGBA8888, tex.FirstImage.FirstMipmap.Format);
        }

        [Fact]
        public void ShortReads_DoNotCorruptMipmapBytes()
        {
            // 每次 Read 只给 3 字节：上游的单次 Read 实现会在这里失败
            var pixels = new byte[64];

            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = (byte) i;

            var bytes = SyntheticTexFactory.CreateRaw(8, 8, pixels);

            using (var raw = new MemoryStream(bytes))
            using (var chunked = new ChunkedReadStream(raw, 3))
            {
                var tex = TexReader.Default.ReadFrom(chunked);

                Assert.Equal(pixels, tex.FirstImage.FirstMipmap.Bytes);
            }
        }

        [Fact]
        public void TruncatedMipmap_ThrowsUnsafeTex()
        {
            var bytes = SyntheticTexFactory.CreateRaw(2, 2, new byte[16]);

            // 砍掉最后 4 字节，使声明的 byteCount 超出实际流长度
            var truncated = new byte[bytes.Length - 4];
            Buffer.BlockCopy(bytes, 0, truncated, 0, truncated.Length);

            using (var stream = new MemoryStream(truncated))
            {
                Assert.Throws<UnsafeTexException>(() => TexReader.Default.ReadFrom(stream));
            }
        }

        [Fact]
        public void OversizedMipmapByteCount_ThrowsUnsafeTex()
        {
            var bytes = SyntheticTexFactory.CreateRaw(1, 1, new byte[4]);

            // 把 byteCount 改成 0x7FFFFFFF（位置：magic 之后的固定头部之后不可预知，这里直接构造）
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                WriteNString(writer, "TEXV0005");
                WriteNString(writer, "TEXI0001");
                writer.Write((int) TexFormat.RGBA8888);
                writer.Write((int) TexFlags.None);
                writer.Write(1);
                writer.Write(1);
                writer.Write(1);
                writer.Write(1);
                writer.Write(0u);
                WriteNString(writer, "TEXB0001");
                writer.Write(1); // image count
                writer.Write(1); // mipmap count
                writer.Write(1); // width
                writer.Write(1); // height
                writer.Write(int.MaxValue);
                writer.Flush();

                Assert.NotEmpty(bytes);
                stream.Position = 0;
                Assert.Throws<UnsafeTexException>(() => TexReader.Default.ReadFrom(stream));
            }
        }

        [Fact]
        public void GifTex_ParsesFrameInfoContainer()
        {
            var bytes = SyntheticTexFactory.CreateGif(
                4, 2, new byte[4 * 2 * 4],
                new[]
                {
                    new SyntheticTexFactory.GifFrameSpec(0, 0.05f, 0, 0, 4, 2),
                    new SyntheticTexFactory.GifFrameSpec(0, 0.10f, 0, 0, 4, 2)
                },
                4, 2);

            var tex = ReadTex(bytes);

            Assert.True(tex.IsGif);
            Assert.NotNull(tex.FrameInfoContainer);
            Assert.Equal(2, tex.FrameInfoContainer.Frames.Count);
            Assert.Equal(4, tex.FrameInfoContainer.GifWidth);
            Assert.Equal(2, tex.FrameInfoContainer.GifHeight);
            Assert.Equal(0.05f, tex.FrameInfoContainer.Frames[0].Frametime, 3);
        }

        [Fact]
        public void StringLengthBeyondSanityLimit_IsRejected()
        {
            // 上游用 Math.Min(size, maxLength) 静默截断；这里必须显式报错
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(2048);
                writer.Write(new byte[2048]);
                writer.Flush();
                stream.Position = 0;

                using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
                    Assert.Throws<InvalidDataException>(() => reader.ReadStringI32Size(1024));
            }
        }

        private static ITex ReadTex(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
                return TexReader.Default.ReadFrom(stream);
        }

        private static void WriteNString(BinaryWriter writer, string value)
        {
            writer.Write(Encoding.UTF8.GetBytes(value));
            writer.Write((byte) 0);
        }
    }
}
