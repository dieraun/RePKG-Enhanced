// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced 测试）
// 覆盖：TEX 元信息 JSON（原版用 Newtonsoft，这里改成 System.Text.Json 手写）。
// 关键点是"取值类型"必须与下游 project.json 的消费方一致：布尔 vs 字符串。

using System;
using System.Text.Json;
using RePKG.Application.Texture;
using RePKG.Core.Texture;
using Xunit;

namespace RePKG.Tests.Texture
{
    public class TexJsonInfoTests
    {
        private readonly TexJsonInfoGenerator _generator = new TexJsonInfoGenerator();

        [Fact]
        public void RawTex_EmitsOriginalFieldTypes()
        {
            var tex = TexConversionTests.CreateTex(MipmapFormat.RGBA8888, new byte[2 * 2 * 4], 2, 2);

            using (var document = JsonDocument.Parse(_generator.GenerateInfo(tex)))
            {
                var root = document.RootElement;

                Assert.Equal(JsonValueKind.True, root.GetProperty("bleedtransparentcolors").ValueKind);
                Assert.Equal(JsonValueKind.False, root.GetProperty("clampuvs").ValueKind);
                Assert.Equal("rgba8888", root.GetProperty("format").GetString());

                // 这三个开关在原版里就是字符串，不是布尔
                Assert.Equal(JsonValueKind.String, root.GetProperty("nomip").ValueKind);
                Assert.Equal("true", root.GetProperty("nomip").GetString());
                Assert.Equal("false", root.GetProperty("nointerpolation").GetString());
                Assert.Equal("false", root.GetProperty("nonpoweroftwo").GetString());

                Assert.False(root.TryGetProperty("spritesheetsequences", out _));
            }
        }

        [Fact]
        public void Flags_AreReflectedInJson()
        {
            var tex = TexConversionTests.CreateTex(
                MipmapFormat.RGBA8888,
                new byte[3 * 5 * 4],
                3, 5,
                flags: TexFlags.ClampUVs | TexFlags.NoInterpolation);

            using (var document = JsonDocument.Parse(_generator.GenerateInfo(tex)))
            {
                var root = document.RootElement;

                Assert.Equal(JsonValueKind.True, root.GetProperty("clampuvs").ValueKind);
                Assert.Equal("true", root.GetProperty("nointerpolation").GetString());
                Assert.Equal("true", root.GetProperty("nonpoweroftwo").GetString());
            }
        }

        [Fact]
        public void GifTex_EmitsSpriteSheetSequence()
        {
            var pixels = new byte[4 * 4 * 4];

            for (var i = 0; i < pixels.Length; i += 4)
                pixels[i + 3] = 255;

            var bytes = TestSupport.SyntheticTexFactory.CreateGif(
                4, 4, pixels,
                new[]
                {
                    new TestSupport.SyntheticTexFactory.GifFrameSpec(0, 0.1f, 0, 0, 2, 2),
                    new TestSupport.SyntheticTexFactory.GifFrameSpec(0, 0.1f, 2, 2, 2, 2)
                },
                4, 4);

            RePKG.Core.Texture.ITex tex;

            using (var stream = new System.IO.MemoryStream(bytes))
                tex = TexReader.Default.ReadFrom(stream);

            using (var document = JsonDocument.Parse(_generator.GenerateInfo(tex)))
            {
                var sequence = document.RootElement.GetProperty("spritesheetsequences");

                Assert.Equal(JsonValueKind.Array, sequence.ValueKind);
                Assert.Equal(1, sequence.GetArrayLength());

                var first = sequence[0];
                Assert.Equal(1, first.GetProperty("duration").GetInt32());
                Assert.Equal(2, first.GetProperty("frames").GetInt32());
                Assert.Equal(4, first.GetProperty("width").GetInt32());
                Assert.Equal(4, first.GetProperty("height").GetInt32());
            }
        }

        [Fact]
        public void AnimatedTexWithoutFrameInfo_Throws()
        {
            var tex = TexConversionTests.CreateTex(
                MipmapFormat.RGBA8888, new byte[16], 2, 2, flags: TexFlags.IsGif);

            Assert.Throws<InvalidOperationException>(() => _generator.GenerateInfo(tex));
        }
    }
}
