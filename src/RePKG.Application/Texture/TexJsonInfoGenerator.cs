// 基于 notscuffed/RePKG (MIT) 二次修改 —— 重写文件（RePKG-Enhanced）
// 改动点：去掉 Newtonsoft.Json（反射式序列化，AOT/裁剪不友好），改用 System.Text.Json 的
//         Utf8JsonWriter 手写输出。字段名、顺序与取值类型（布尔 vs 字符串）与原版保持一致。

using System;
using System.Buffers;
using System.Text;
using System.Text.Json;
using RePKG.Core.Texture;

namespace RePKG.Application.Texture
{
    public class TexJsonInfoGenerator : ITexJsonInfoGenerator
    {
        public string GenerateInfo(ITex tex)
        {
            if (tex == null) throw new ArgumentNullException(nameof(tex));

            if (tex.IsGif && tex.FrameInfoContainer == null)
                throw new InvalidOperationException("TEX is animated but doesn't have frame info container");

            var buffer = new ArrayBufferWriter<byte>(512);

            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();

                // 与原版一致：bleedtransparentcolors 是布尔，其余开关是字符串 "true"/"false"
                writer.WriteBoolean("bleedtransparentcolors", true);
                writer.WriteBoolean("clampuvs", tex.HasFlag(TexFlags.ClampUVs));
                writer.WriteString("format", tex.Header.Format.ToString().ToLowerInvariant());
                writer.WriteString("nomip", BoolLiteral(tex.FirstImage.Mipmaps.Count == 1));
                writer.WriteString("nointerpolation", BoolLiteral(tex.HasFlag(TexFlags.NoInterpolation)));
                writer.WriteString("nonpoweroftwo", BoolLiteral(
                    !NumberIsPowerOfTwo(tex.Header.ImageWidth) || !NumberIsPowerOfTwo(tex.Header.ImageHeight)));

                if (tex.IsGif)
                {
                    writer.WriteStartArray("spritesheetsequences");
                    writer.WriteStartObject();
                    writer.WriteNumber("duration", 1); // 原版备注：用途不明，这里保持原值
                    writer.WriteNumber("frames", tex.FrameInfoContainer.Frames.Count);
                    writer.WriteNumber("width", tex.FrameInfoContainer.GifWidth);
                    writer.WriteNumber("height", tex.FrameInfoContainer.GifHeight);
                    writer.WriteEndObject();
                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        private static string BoolLiteral(bool value) => value ? "true" : "false";

        private static bool NumberIsPowerOfTwo(int n)
        {
            if (n == 0)
                return false;

            while (n != 1)
            {
                if (n % 2 != 0)
                    return false;

                n /= 2;
            }

            return true;
        }
    }
}
