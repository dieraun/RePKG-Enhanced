using System;
using System.Collections.Generic;
using System.IO;
using RePKG.Core.Package;
using RePKG.Core.Package.Interfaces;

namespace RePKG.Application.Package
{
    // 基于 notscuffed/RePKG (MIT) 二次修改
    // 改动点（本次修复的核心）：
    //   1. 删除 PopulateEntriesWithData：原版在解析时对每个条目执行
    //      `entry.Bytes = reader.ReadBytes(entry.Length)`，把**整包所有条目**读进内存，
    //      因此提取一个 1 GB 的条目也要先把整个包载入，直接导致 OOM。
    //      现在 ReadFrom 只解析条目表（FullPath/Offset/Length/Type），不触碰数据区，
    //      解析完成后流的 Position 恰好停在数据区起点，由 PackageFileSource 按条目流式读取。
    //   2. 删除 ReadEntryBytes 开关：不再存在"读字节"的分支，避免调用方误用整包载入路径。
    //   3. Offset/Length 按 uint32 读取并存为 long：原版用 ReadInt32，> 2 GiB 的偏移会变成负数，
    //      且 `entry.Offset + dataStart` 的 int 相加会溢出。
    //   4. 增加轻量防御：条目数上限、名称长度、偏移+长度是否越界（在流可 seek 时校验）。
    public class PackageReader : IPackageReader
    {
        /// <summary>单个 pkg 条目数的上限，避免畸形包一次性构造超大列表。</summary>
        public int MaximumEntryCount { get; set; } = 1_000_000;

        /// <summary>条目是否必须落在流范围内（对不可 seek 的流自动跳过校验）。</summary>
        public bool ValidateEntryRanges { get; set; } = true;

        public Core.Package.Package ReadFrom(BinaryReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            var stream = reader.BaseStream;
            var packageStart = stream.Position;

            var package = new Core.Package.Package
            {
                Magic = reader.ReadStringI32Size(maxLength: 32)
            };

            ReadEntries(package.Entries, reader);

            var dataStart = stream.Position;
            package.HeaderSize = checked((int) (dataStart - packageStart));

            if (ValidateEntryRanges)
                ValidateRanges(package.Entries, dataStart, stream);

            // 解析结束时把流放回数据区起点，方便调用方直接从 DataStart 开始流式读取
            stream.Seek(dataStart, SeekOrigin.Begin);
            return package;
        }

        private void ReadEntries(List<PackageEntry> list, BinaryReader reader)
        {
            var entryCount = reader.ReadUInt32();

            if (entryCount > (uint) MaximumEntryCount)
                throw new InvalidDataException(
                    $"Package declares {entryCount} entries which exceeds the sanity limit of {MaximumEntryCount}");

            for (var i = 0u; i < entryCount; i++)
            {
                // 上游用 maxLength: 255 并在超长时静默截断，会把条目名改为错误路径。
                // 这里把上限放宽到 1024（远超正常资源路径），仅在真正异常时拒绝。
                var fullPath = reader.ReadStringI32Size(maxLength: 1024);

                list.Add(new PackageEntry
                {
                    FullPath = fullPath,
                    Offset = reader.ReadUInt32(),   // 无符号：原版 ReadInt32 会在 > 2 GiB 时变成负数
                    Length = reader.ReadUInt32(),
                    Type = PackageEntryTypeGetter.GetFromFileName(fullPath)
                });
            }
        }

        private static void ValidateRanges(List<PackageEntry> entries, long dataStart, Stream stream)
        {
            long streamLength;
            try
            {
                streamLength = stream.Length;
            }
            catch (NotSupportedException)
            {
                return; // 不可 seek 的流无法预先校验
            }

            var bodyAvailable = streamLength - dataStart;
            if (bodyAvailable < 0)
                throw new InvalidDataException("Package header extends past the end of the stream");

            foreach (var entry in entries)
            {
                if (entry.Offset + entry.Length > bodyAvailable)
                    throw new InvalidDataException(
                        $"Entry `{entry.FullPath}` (offset {entry.Offset}, length {entry.Length}) " +
                        $"exceeds the package body size of {bodyAvailable} bytes — the package is truncated or corrupt");
            }
        }
    }
}
