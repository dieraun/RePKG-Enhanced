// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 导出链路的可插拔转换器：让 TEX→PNG 这类"条目内容变换"也走同一条队列与同一套原子写入，
// 而不是让 CLI / GUI 各自在入口层写一套条目循环。

using System;
using System.Threading;
using RePKG.Application.Package;
using RePKG.Core.Package;

namespace RePKG.Shared.Export
{
    /// <summary>一次条目转换的结果。</summary>
    public readonly struct ConversionResult
    {
        public ConversionResult(string? primaryPath, long bytesWritten)
        {
            PrimaryPath = primaryPath;
            BytesWritten = bytesWritten;
        }

        /// <summary>主输出文件路径；为 null 表示按"不覆盖"规则跳过。</summary>
        public string? PrimaryPath { get; }

        public long BytesWritten { get; }

        public bool Skipped => PrimaryPath == null;

        public static ConversionResult Skip => new ConversionResult(null, 0);
    }

    /// <summary>
    /// 条目转换器。实现方负责按 <see cref="ExportOptions"/> 决定是否覆盖，并自行原子写入输出。
    /// </summary>
    public interface IEntryConverter
    {
        /// <summary>是否由本转换器处理该条目。</summary>
        bool CanConvert(PackageEntry entry);

        /// <summary>
        /// 把条目转换后写入 <paramref name="targetPath"/> 所在位置。
        /// <paramref name="targetPath"/> 是队列按条目路径推导出的目标路径（扩展名未必正确，实现方可改写）。
        /// </summary>
        ConversionResult Convert(
            PackageFileSource source,
            PackageEntry entry,
            string targetPath,
            ExportOptions options,
            CancellationToken cancellationToken);
    }
}
