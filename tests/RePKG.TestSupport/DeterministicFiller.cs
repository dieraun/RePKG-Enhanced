using System;

namespace RePKG.TestSupport
{
    /// <summary>
    /// 可重复的伪随机填充器：内容只由 (seed, 绝对偏移) 决定，因此
    /// 写入端与校验端都能在**不落盘、不驻留内存**的情况下生成同样的字节。
    ///
    /// 做法：先用 SplitMix64 生成一个 64 KiB 的模板块，然后按 64 KiB 环绕重复。
    /// 这样填充是 memcpy 级速度（1 GiB 约 1–2 秒），且完全确定。
    /// </summary>
    public sealed class DeterministicFiller
    {
        public const int BlockSize = 64 * 1024;
        private const long BlockMask = BlockSize - 1;

        private readonly ulong _seed;
        private readonly byte[] _template;

        public DeterministicFiller(ulong seed = 0x9E37_79B9_7F4A_7C15UL)
        {
            _seed = seed;
            _template = new byte[BlockSize];
            BuildTemplate();
        }

        public ulong Seed => _seed;

        private void BuildTemplate()
        {
            var state = _seed;
            for (var i = 0; i < BlockSize; i += 8)
            {
                var value = SplitMix64(ref state);
                for (var b = 0; b < 8 && i + b < BlockSize; b++)
                    _template[i + b] = (byte) (value >> (b * 8));
            }
        }

        /// <summary>把从 <paramref name="absoluteOffset"/> 开始的字节填进 <paramref name="destination"/>。</summary>
        public void Fill(Span<byte> destination, long absoluteOffset)
        {
            var written = 0;
            var index = (int) (absoluteOffset & BlockMask);

            while (written < destination.Length)
            {
                var chunk = Math.Min(destination.Length - written, BlockSize - index);
                _template.AsSpan(index, chunk).CopyTo(destination.Slice(written));
                written += chunk;
                index = (int) ((index + chunk) & BlockMask);
            }
        }

        private static ulong SplitMix64(ref ulong state)
        {
            state += 0x9E37_79B9_7F4A_7C15UL;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EBUL;
            return z ^ (z >> 31);
        }
    }
}
