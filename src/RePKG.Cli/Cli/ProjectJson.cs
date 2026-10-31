// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 读取 pkg 内的 project.json：-n 需要 title，-c 需要把原文件写出。
// 只读这一条小条目，且带长度上限，不碰数据区其余部分。

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using RePKG.Application.Package;
using RePKG.Core.Package;
using RePKG.Shared.IO;

namespace RePKG.Cli
{
    internal static class ProjectJson
    {
        public const string EntryName = "project.json";
        private const long MaximumBytes = 4L << 20;

        /// <summary>读取 title；条目缺失、过大、JSON 异常或没有 title 时返回 null。</summary>
        public static string? TryReadTitle(PackageFileSource source)
        {
            var entry = FindProjectEntry(source);

            if (entry == null || entry.Length > MaximumBytes)
                return null;

            try
            {
                using (var stream = source.OpenEntry(entry))
                using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false))
                using (var document = JsonDocument.Parse(reader.ReadToEnd()))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                        return null;

                    if (!document.RootElement.TryGetProperty("title", out var title))
                        return null;

                    return title.ValueKind == JsonValueKind.String ? title.GetString() : null;
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is InvalidDataException)
            {
                return null;
            }
        }

        /// <summary>把 project.json 原样写到 <paramref name="targetDirectory"/>（原子写入）。</summary>
        public static bool TryCopy(PackageFileSource source, string targetDirectory, bool overwrite)
        {
            var entry = FindProjectEntry(source);

            if (entry == null || entry.Length > MaximumBytes)
                return false;

            var target = Path.Combine(targetDirectory, EntryName);

            if (!overwrite && File.Exists(target))
                return true;

            using (var stream = source.OpenEntry(entry))
            using (var writer = AtomicFileWriter.Create(target))
            {
                try
                {
                    stream.CopyTo(writer.Stream);
                }
                catch
                {
                    writer.Abort();
                    throw;
                }

                writer.Commit();
            }

            return true;
        }

        private static PackageEntry? FindProjectEntry(PackageFileSource source)
        {
            foreach (var entry in source.Entries)
            {
                if (string.Equals(entry.FullPath, EntryName, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }

            return null;
        }
    }
}
