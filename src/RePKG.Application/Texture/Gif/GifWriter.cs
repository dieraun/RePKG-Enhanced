// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 改动点：原版用 ImageSharp 的 GifEncoder 一次性编码整段动画（必须把所有帧物化在内存里，
//         且 SkiaSharp 没有等价能力，见 issue #1130）；这里自研最小 GIF89a 写入器：
//         逐帧写盘、局部色表、LZW 变长编码、NETSCAPE 循环扩展。

using System;
using System.Collections.Generic;
using System.IO;

namespace RePKG.Application.Texture.Gif
{
    /// <summary>
    /// 最小 GIF89a 编码器：顺序写帧，不缓存历史帧。
    /// 只支持本项目需要的特性：局部色表、透明色、帧延迟、循环次数。
    /// </summary>
    internal sealed class GifWriter : IDisposable
    {
        private readonly Stream _destination;
        private readonly int _width;
        private readonly int _height;
        private bool _disposed;

        public GifWriter(Stream destination, int width, int height, int loopCount = 0)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (width <= 0 || width > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0 || height > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(height));

            _destination = destination;
            _width = width;
            _height = height;

            WriteHeader(loopCount);
        }

        private void WriteHeader(int loopCount)
        {
            // GIF89a 签名
            _destination.Write(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, 0, 6);

            WriteUInt16(_width);
            WriteUInt16(_height);

            // 无全局色表；颜色分辨率 8 bit（bits 4..6 = 111）
            _destination.WriteByte(0x70);
            _destination.WriteByte(0x00); // 背景色索引
            _destination.WriteByte(0x00); // 像素宽高比

            if (loopCount < 0) return;

            // NETSCAPE2.0 循环扩展
            _destination.WriteByte(0x21);
            _destination.WriteByte(0xFF);
            _destination.WriteByte(0x0B);

            var application = System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0");
            _destination.Write(application, 0, application.Length);

            _destination.WriteByte(0x03);
            _destination.WriteByte(0x01);
            WriteUInt16(loopCount);
            _destination.WriteByte(0x00);
        }

        /// <summary>
        /// 写入一帧。索引图按行优先排列（左→右、上→下），长度必须等于 width*height。
        /// </summary>
        public void WriteFrame(
            byte[] indices,
            int width,
            int height,
            byte[] palette,
            int colorCount,
            int delayCentiseconds,
            int transparentIndex,
            int left = 0,
            int top = 0)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GifWriter));
            if (indices == null) throw new ArgumentNullException(nameof(indices));
            if (indices.Length != width * height)
                throw new ArgumentException("索引图长度与帧尺寸不匹配", nameof(indices));

            var tableSize = GetTableSize(colorCount);

            // 图形控制扩展：disposal = 2（恢复为背景），可带透明索引
            _destination.WriteByte(0x21);
            _destination.WriteByte(0xF9);
            _destination.WriteByte(0x04);
            _destination.WriteByte((byte) ((2 << 2) | (transparentIndex >= 0 ? 1 : 0)));
            WriteUInt16(Math.Max(0, Math.Min(ushort.MaxValue, delayCentiseconds)));
            _destination.WriteByte((byte) (transparentIndex >= 0 ? transparentIndex : 0));
            _destination.WriteByte(0x00);

            // 图像描述符：使用局部色表
            _destination.WriteByte(0x2C);
            WriteUInt16(left);
            WriteUInt16(top);
            WriteUInt16(width);
            WriteUInt16(height);
            _destination.WriteByte((byte) (0x80 | (GetSizeBits(tableSize))));

            WritePalette(palette, colorCount, tableSize);

            var minCodeSize = Math.Max(2, GetSizeBits(tableSize) + 1);
            _destination.WriteByte((byte) minCodeSize);
            WriteLzwData(indices, minCodeSize);
            _destination.WriteByte(0x00);
        }

        private void WritePalette(byte[] palette, int colorCount, int tableSize)
        {
            var bytes = new byte[tableSize * 3];

            if (palette != null)
            {
                var copy = Math.Min(colorCount * 3, Math.Min(palette.Length, bytes.Length));
                Array.Copy(palette, 0, bytes, 0, copy);
            }

            _destination.Write(bytes, 0, bytes.Length);
        }

        private static int GetTableSize(int colorCount)
        {
            var size = 2;

            while (size < colorCount && size < 256) size <<= 1;

            return size;
        }

        private static int GetSizeBits(int tableSize)
        {
            var bits = 0;
            var size = tableSize;

            while (size > 1)
            {
                size >>= 1;
                bits++;
            }

            return Math.Max(0, bits - 1);
        }

        #region LZW

        private void WriteLzwData(byte[] indices, int minCodeSize)
        {
            var clearCode = 1 << minCodeSize;
            var endCode = clearCode + 1;
            var codeSize = minCodeSize + 1;
            var nextCode = endCode + 1;

            var dictionary = new Dictionary<int, int>(4096);
            var block = new byte[255];
            var blockLength = 0;

            uint bitBuffer = 0;
            var bitCount = 0;

            void Flush()
            {
                if (blockLength == 0) return;

                _destination.WriteByte((byte) blockLength);
                _destination.Write(block, 0, blockLength);
                blockLength = 0;
            }

            void Emit(int code)
            {
                bitBuffer |= (uint) code << bitCount;
                bitCount += codeSize;

                while (bitCount >= 8)
                {
                    block[blockLength++] = (byte) (bitBuffer & 0xFF);
                    bitBuffer >>= 8;
                    bitCount -= 8;

                    if (blockLength == block.Length) Flush();
                }
            }

            Emit(clearCode);

            var prefix = (int) indices[0];

            for (var i = 1; i < indices.Length; i++)
            {
                var next = indices[i];
                var key = (prefix << 8) | next;

                if (dictionary.TryGetValue(key, out var code))
                {
                    prefix = code;
                    continue;
                }

                Emit(prefix);

                if (nextCode < 4096)
                {
                    dictionary[key] = nextCode++;

                    if (nextCode > (1 << codeSize) && codeSize < 12)
                        codeSize++;
                }
                else
                {
                    Emit(clearCode);
                    dictionary.Clear();
                    codeSize = minCodeSize + 1;
                    nextCode = endCode + 1;
                }

                prefix = next;
            }

            Emit(prefix);
            Emit(endCode);

            if (bitCount > 0)
            {
                block[blockLength++] = (byte) (bitBuffer & 0xFF);

                if (blockLength == block.Length) Flush();
            }

            Flush();
        }

        #endregion

        private void WriteUInt16(int value)
        {
            _destination.WriteByte((byte) (value & 0xFF));
            _destination.WriteByte((byte) ((value >> 8) & 0xFF));
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _destination.WriteByte(0x3B); // trailer
            _destination.Flush();
        }
    }
}
