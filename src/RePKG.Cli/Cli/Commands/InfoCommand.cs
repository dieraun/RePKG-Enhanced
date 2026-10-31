// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// info 子命令：只读条目表（不碰数据区），列出条目清单；--json 给脚本用。

using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Json;
using RePKG.Application.Package;

namespace RePKG.Cli
{
    internal static class InfoCommand
    {
        public static ExitCode Run(CliOptions options, TextWriter output)
        {
            var packages = InputResolver.CollectPackages(options.Inputs, options.Recursive);
            var failed = false;

            foreach (var package in packages)
            {
                try
                {
                    using (var source = PackageFileSource.Open(package, validateEntryRanges: false))
                    {
                        if (options.Json)
                            WriteJson(output, package, source);
                        else
                            WriteText(output, package, source);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                {
                    output.WriteLine($"[失败] {package}：{ex.Message}");
                    failed = true;
                }
            }

            return failed ? ExitCode.PartialFailure : ExitCode.Success;
        }

        private static void WriteText(TextWriter output, string packagePath, PackageFileSource source)
        {
            output.WriteLine($"pkg   : {packagePath}");
            output.WriteLine($"size  : {ConsoleReporter.FormatBytes(source.FileLength)} ({source.FileLength} 字节)");
            output.WriteLine($"head  : {source.HeaderSize} 字节，数据区起点 {source.DataStart}");
            output.WriteLine($"items : {source.Entries.Count}");

            long total = 0;

            foreach (var entry in source.Entries)
            {
                total += entry.Length;
                output.WriteLine($"  {entry.Length,14}  {entry.FullPath}");
            }

            output.WriteLine($"total : {total} 字节");
            output.WriteLine();
        }

        private static void WriteJson(TextWriter output, string packagePath, PackageFileSource source)
        {
            var buffer = new ArrayBufferWriter<byte>(4096);

            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("packagePath", packagePath);
                writer.WriteNumber("fileLength", source.FileLength);
                writer.WriteNumber("headerSize", source.HeaderSize);
                writer.WriteNumber("dataStart", source.DataStart);
                writer.WriteNumber("entryCount", source.Entries.Count);
                writer.WriteStartArray("entries");

                foreach (var entry in source.Entries)
                {
                    writer.WriteStartObject();
                    writer.WriteString("fullPath", entry.FullPath);
                    writer.WriteNumber("offset", entry.Offset);
                    writer.WriteNumber("length", entry.Length);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
        }
    }
}
