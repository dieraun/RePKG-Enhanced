namespace RePKG.Application
{
    // 基于 notscuffed/RePKG (MIT) 二次修改：新增流式读取相关常量。
    public static class Constants
    {
        public const int MaximumFrameCount = 100_000;
        public const int MaximumImageCount = 100;
        public const int MaximumMipmapCount = 32;
        public const int MaximumMipmapByteCount = 250_000_000; // 250 MB

        /// <summary>
        /// 条目流式复制的默认块大小。峰值内存 ≈ 该值（外加一次数组头），与条目大小和包总体积无关。
        /// 1 MiB 在 HDD/SSD/网络盘上都能打满带宽，同时把分配量控制在 1 MiB 量级。
        /// </summary>
        public const int DefaultCopyBufferSize = 1 << 20;

        /// <summary>复制的块大小下限，防止调用方传入 1 字节导致 syscall 风暴。</summary>
        public const int MinimumCopyBufferSize = 4 << 10;
    }
}
