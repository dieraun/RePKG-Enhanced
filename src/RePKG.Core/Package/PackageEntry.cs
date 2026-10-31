using System.IO;
using RePKG.Core.Package.Enums;

namespace RePKG.Core.Package
{
    // 基于 notscuffed/RePKG (MIT) 二次修改
    // 改动点：
    //   1. Offset / Length 由 int 改为 long。pkg 格式里这两个字段是无符号 32 位，
    //      原版用 int 会让数据区上限被限制在约 2 GiB，且 > 2 GiB 时 Offset + dataStart 会溢出。
    //      内部统一用 long 承载（可表示 4 GiB 数据区有余），读写时按 uint32 解释。
    //   2. Bytes 标记为过时：解析链路不再填充整条目字节，改用流式读取
    //      （RePKG.Application.Package.PackageFileSource / PackageEntryStream）。
    public class PackageEntry
    {
        public string FullPath { get; set; }
        public long Offset { get; set; }
        public long Length { get; set; }

        /// <summary>
        /// 条目字节的完整副本。解析流程不再填充它（大文件会 OOM）。
        /// 仅在写包（PackageWriter）等显式场景使用；提取请走 PackageFileSource.OpenEntry 流式读取。
        /// </summary>
        public byte[] Bytes { get; set; }

        public EntryType Type { get; set; }

        public string Name => Path.GetFileNameWithoutExtension(FullPath);
        public string Extension => Path.GetExtension(FullPath);
        public string DirectoryPath => Path.GetDirectoryName(FullPath);
    }
}
