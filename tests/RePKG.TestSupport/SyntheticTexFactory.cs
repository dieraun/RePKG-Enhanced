// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced 测试支撑）
// 用途：在没有真实 Wallpaper Engine 样本的前提下，按 TEX 容器格式手工合成最小合法 tex 字节，
//       用于验证解析链路（TEXV0005/TEXI0001 + TEXB0001/TEXB0004 + TEXS0003）。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RePKG.Core.Texture;

namespace RePKG.TestSupport
{
    public static class SyntheticTexFactory
    {
        public readonly record struct GifFrameSpec(
            int ImageId,
            float Frametime,
            float X,
            float Y,
            float Width,
            float Height);

        /// <summary>
        /// 合成一张单图、单 mipmap 的原始格式 TEX（TEXB0001 容器，字节序为小端）。
        /// </summary>
        public static byte[] CreateRaw(
            int width,
            int height,
            byte[] pixels,
            TexFormat format = TexFormat.RGBA8888,
            TexFlags flags = TexFlags.None,
            int imageWidth = -1,
            int imageHeight = -1)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                WriteHeader(writer, format, flags, width, height, imageWidth, imageHeight);
                WriteNString(writer, "TEXB0001");

                writer.Write(1); // image count
                writer.Write(1); // mipmap count
                writer.Write(width);
                writer.Write(height);
                writer.Write(pixels.Length);
                writer.Write(pixels);
                writer.Flush();

                return stream.ToArray();
            }
        }

        /// <summary>
        /// 合成一张动画 TEX：一个 spritesheet 图 + TEXS0003 帧信息。
        /// </summary>
        public static byte[] CreateGif(
            int width,
            int height,
            byte[] rgba,
            IReadOnlyList<GifFrameSpec> frames,
            int gifWidth,
            int gifHeight,
            TexFormat format = TexFormat.RGBA8888)
        {
            if (rgba == null) throw new ArgumentNullException(nameof(rgba));
            if (frames == null) throw new ArgumentNullException(nameof(frames));

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                WriteHeader(writer, format, TexFlags.IsGif, width, height, width, height);
                WriteNString(writer, "TEXB0001");

                writer.Write(1); // image count
                writer.Write(1); // mipmap count
                writer.Write(width);
                writer.Write(height);
                writer.Write(rgba.Length);
                writer.Write(rgba);

                WriteNString(writer, "TEXS0003");
                writer.Write(frames.Count);
                writer.Write(gifWidth);
                writer.Write(gifHeight);

                foreach (var frame in frames)
                {
                    writer.Write(frame.ImageId);
                    writer.Write(frame.Frametime);
                    writer.Write(frame.X);
                    writer.Write(frame.Y);
                    writer.Write(frame.Width);
                    writer.Write(frame.Height);
                    writer.Write(0f); // WidthY
                    writer.Write(0f); // HeightX
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        private static void WriteHeader(
            BinaryWriter writer,
            TexFormat format,
            TexFlags flags,
            int width,
            int height,
            int imageWidth,
            int imageHeight)
        {
            WriteNString(writer, "TEXV0005");
            WriteNString(writer, "TEXI0001");

            writer.Write((int) format);
            writer.Write((int) flags);
            writer.Write(width);
            writer.Write(height);
            writer.Write(imageWidth < 0 ? width : imageWidth);
            writer.Write(imageHeight < 0 ? height : imageHeight);
            writer.Write(0u); // UnkInt0
        }

        private static void WriteNString(BinaryWriter writer, string value)
        {
            writer.Write(Encoding.UTF8.GetBytes(value));
            writer.Write((byte) 0);
        }
    }

    /// <summary>
    /// 每次 Read 最多只返回少量字节的流，用来暴露「单次 Read 短读」类断言——
    /// 真实文件流在缓冲边界、管道与网络流上都会出现短读。
    /// </summary>
    public sealed class ChunkedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly int _maxChunk;

        public ChunkedReadStream(Stream inner, int maxChunk)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));

            if (maxChunk <= 0) throw new ArgumentOutOfRangeException(nameof(maxChunk));

            _maxChunk = maxChunk;
        }

        public override bool CanRead => true;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
            => _inner.Read(buffer, offset, Math.Min(count, _maxChunk));

        public override int Read(Span<byte> buffer)
            => _inner.Read(buffer.Slice(0, Math.Min(buffer.Length, _maxChunk)));

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();

            base.Dispose(disposing);
        }
    }
}
