using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RePKG.Shared.Logging
{
    public enum ExportLogLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>一条导出日志。字段刻意保持扁平，便于 CLI 用 jq 消费。</summary>
    public readonly struct ExportLogRecord
    {
        public ExportLogRecord(
            ExportLogLevel level,
            string @event,
            string? packagePath = null,
            string? entryPath = null,
            long bytes = 0,
            string? message = null)
        {
            Level = level;
            Event = @event;
            PackagePath = packagePath;
            EntryPath = entryPath;
            Bytes = bytes;
            Message = message;
            Timestamp = DateTimeOffset.UtcNow;
        }

        public DateTimeOffset Timestamp { get; }
        public ExportLogLevel Level { get; }
        public string Event { get; }
        public string? PackagePath { get; }
        public string? EntryPath { get; }
        public long Bytes { get; }
        public string? Message { get; }
    }

    public interface IExportLog : IDisposable
    {
        void Write(in ExportLogRecord record);
    }

    /// <summary>不记录任何内容（禁用日志时使用，避免各处写 null 判断）。</summary>
    public sealed class NullExportLog : IExportLog
    {
        public static readonly NullExportLog Instance = new NullExportLog();

        public void Write(in ExportLogRecord record) { }

        public void Dispose() { }
    }

    /// <summary>
    /// 行分隔 JSON（JSONL）日志：一行一个对象，写入是线程安全的，追加式。
    /// 用 <see cref="Utf8JsonWriter"/> 手工序列化，不依赖反射 → Native AOT 安全。
    /// </summary>
    public sealed class JsonlExportLog : IExportLog
    {
        private readonly object _gate = new object();
        private readonly FileStream _stream;
        private readonly ArrayBufferWriter<byte> _buffer = new ArrayBufferWriter<byte>(512);
        private readonly Utf8JsonWriter _json;
        private bool _disposed;

        public JsonlExportLog(string path, bool append = false)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("日志路径为空", nameof(path));

            var full = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(full);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            _stream = new FileStream(
                full,
                append ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 16 * 1024);

            FilePath = full;

            _json = new Utf8JsonWriter(_buffer, new JsonWriterOptions
            {
                Indented = false,
                // 中文路径不做 \uXXXX 转义，日志可读
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                SkipValidation = false
            });
        }

        /// <summary>日志文件绝对路径。</summary>
        public string FilePath { get; }

        public void Write(in ExportLogRecord record)
        {
            lock (_gate)
            {
                if (_disposed) return;

                _buffer.Clear();
                _json.Reset(_buffer);

                _json.WriteStartObject();
                _json.WriteString("ts", record.Timestamp.ToString("O"));
                _json.WriteString("level", LevelText(record.Level));
                _json.WriteString("event", record.Event);

                if (record.PackagePath != null) _json.WriteString("package", record.PackagePath);
                if (record.EntryPath != null) _json.WriteString("entry", record.EntryPath);
                if (record.Bytes != 0) _json.WriteNumber("bytes", record.Bytes);
                if (record.Message != null) _json.WriteString("message", record.Message);

                _json.WriteEndObject();
                _json.Flush();

                _buffer.Write("\n"u8);
                _stream.Write(_buffer.WrittenSpan);
            }
        }

        private static string LevelText(ExportLogLevel level) => level switch
        {
            ExportLogLevel.Info => "info",
            ExportLogLevel.Warning => "warn",
            ExportLogLevel.Error => "error",
            _ => "info"
        };

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;

                try
                {
                    _stream.Flush(flushToDisk: false);
                }
                catch (IOException)
                {
                }
                finally
                {
                    _json.Dispose();
                    _stream.Dispose();
                    _disposed = true;
                }
            }
        }
    }

    /// <summary>给 <see cref="IExportLog"/> 加一点调用糖，避免到处构造 record。</summary>
    public static class ExportLogExtensions
    {
        public static void Info(this IExportLog log, string @event, string? packagePath = null,
            string? entryPath = null, long bytes = 0, string? message = null)
            => log.Write(new ExportLogRecord(ExportLogLevel.Info, @event, packagePath, entryPath, bytes, message));

        public static void Warn(this IExportLog log, string @event, string? packagePath = null,
            string? entryPath = null, string? message = null)
            => log.Write(new ExportLogRecord(ExportLogLevel.Warning, @event, packagePath, entryPath, 0, message));

        public static void Error(this IExportLog log, string @event, string? packagePath = null,
            string? entryPath = null, string? message = null)
            => log.Write(new ExportLogRecord(ExportLogLevel.Error, @event, packagePath, entryPath, 0, message));
    }
}
