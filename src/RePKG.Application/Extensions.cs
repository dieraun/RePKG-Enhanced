using System;
using System.IO;
using System.Text;

namespace RePKG.Application
{
    internal static class Extensions
    {
        public static string ReadNString(this BinaryReader reader, int maxLength = -1)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            var builder = new StringBuilder(maxLength <= 0 ? 16 : maxLength);
            var c = reader.ReadChar();

            while (c != '\0' && (maxLength == -1 || builder.Length < maxLength))
            {
                builder.Append(c);
                c = reader.ReadChar();
            }

            return builder.ToString();
        }

        public static void WriteNString(this BinaryWriter writer, string input)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (input == null) throw new ArgumentNullException(nameof(input));

            writer.Write(Encoding.UTF8.GetBytes(input));
            writer.Write((byte) 0);
        }

        // 基于 notscuffed/RePKG (MIT) 二次修改
        // 改动点：
        //   1. 原版在 size > maxLength 时执行 `size = Math.Min(size, maxLength)`，
        //      把超长路径**静默截断**后继续解析，导致条目名错乱且不报错。
        //      现在超长即抛 InvalidDataException（畸形包应当可见地失败）。
        //   2. 原版直接用 reader.ReadBytes(size)，当流被截断时 ReadBytes 会静默返回更少的字节，
        //      UTF8 解码后得到一个更短的字符串——这是"截断"类缺陷的另一个来源。
        //      现在先校验剩余字节数，不足即抛。
        public static string ReadStringI32Size(this BinaryReader reader, int maxLength = -1)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            var size = reader.ReadInt32();

            if (size < 0)
                throw new InvalidDataException($"String size cannot be negative (got {size})");

            if (maxLength > -1 && size > maxLength)
                throw new InvalidDataException(
                    $"String size {size} exceeds the sanity limit of {maxLength} bytes");

            if (size == 0)
                return string.Empty;

            var stream = reader.BaseStream;
            if (stream.CanSeek)
            {
                var remaining = stream.Length - stream.Position;
                if (size > remaining)
                    throw new EndOfStreamException(
                        $"Declared string size {size} exceeds the {remaining} bytes left in the stream — the package is truncated");
            }

            var bytes = new byte[size];
            var read = 0;
            while (read < size)
            {
                var n = stream.Read(bytes, read, size - read);
                if (n <= 0)
                    throw new EndOfStreamException(
                        $"Unexpected end of stream while reading a {size}-byte string ({read} bytes read)");
                read += n;
            }

            return Encoding.UTF8.GetString(bytes);
        }

        // 基于 notscuffed/RePKG (MIT) 二次修改
        // 改动点：原版 `writer.Write(input.Length)` 写的是**字符数**，而正文写的是 UTF-8 字节数，
        //         非 ASCII 名称回写后长度字段与实际字节数不一致。改为写字节数。
        public static void WriteStringI32Size(this BinaryWriter writer, string input)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (input == null) throw new ArgumentNullException(nameof(input));

            var bytes = Encoding.UTF8.GetBytes(input);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
    }
}
