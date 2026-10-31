// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 资源清单（manifest）：把条目表导出成 JSON，供脚本核对「包里有什么」。
// CLI（`info --json`、`extract --manifest`）与 GUI 共用这一份实现，避免两处格式漂移。

using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Json;
using RePKG.Application.Package;

namespace RePKG.Shared.Catalog
{
    public static class PackageManifest
    {
        /// <summary>清单文件名（写在每个包的输出目录里）。</summary>
        public const string FileName = "manifest.json";

        /// <summary>生成缩进 JSON（不落盘时用，例如 CLI `info --json`）。</summary>
        public static string ToJson(PackageFileSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var buffer = new ArrayBufferWriter<byte>(4096);

            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
                WriteJson(writer, source);

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        /// <summary>把清单写进流（UTF-8，无 BOM）。</summary>
        public static void Write(PackageFileSource source, Stream destination)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            var buffer = new ArrayBufferWriter<byte>(4096);

            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
                WriteJson(writer, source);

            destination.Write(buffer.WrittenSpan);
            destination.WriteByte((byte) '\n');
        }

        private static void WriteJson(Utf8JsonWriter writer, PackageFileSource source)
        {
            writer.WriteStartObject();
            writer.WriteString("packagePath", source.Path);
            writer.WriteNumber("fileLength", source.FileLength);
            writer.WriteNumber("headerSize", source.HeaderSize);
            writer.WriteNumber("dataStart", source.DataStart);
            writer.WriteNumber("entryCount", source.Entries.Count);

            long total = 0;

            writer.WriteStartArray("entries");

            foreach (var entry in source.Entries)
            {
                total += entry.Length;

                writer.WriteStartObject();
                writer.WriteString("fullPath", entry.FullPath);
                writer.WriteNumber("offset", entry.Offset);
                writer.WriteNumber("length", entry.Length);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteNumber("totalBytes", total);
            writer.WriteEndObject();
        }
    }
}
