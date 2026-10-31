// 基于 notscuffed/RePKG (MIT) 二次修改：本文件为 RePKG-Enhanced 新增，不来自原版。

using System;
using System.Buffers;
using System.IO;
using System.Text.Json;
using RePKG.Shared.Localization;

namespace RePKG.Shared.Settings;

/// <summary>可持久化的用户设置（纯本地，不上传）。</summary>
public sealed class AppSettings
{
    /// <summary>界面语言；默认跟随系统。</summary>
    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>克隆一份，避免外部改到已保存的实例。</summary>
    public AppSettings Clone() => new() { Language = Language };
}

/// <summary>
/// <c>%APPDATA%\RePKG-Enhanced\settings.json</c> 读写。
/// 读失败（文件损坏、权限不足）一律退回默认值——设置永远不能阻止程序启动。
/// </summary>
public sealed class SettingsStore
{
    /// <summary>设置目录名。</summary>
    public const string FolderName = "RePKG-Enhanced";

    /// <summary>设置文件名。</summary>
    public const string FileName = "settings.json";

    /// <summary>默认设置目录（<c>%APPDATA%\RePKG-Enhanced</c>）。</summary>
    public static string DefaultDirectory
        => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);

    /// <summary>默认设置文件路径。</summary>
    public static string DefaultPath => System.IO.Path.Combine(DefaultDirectory, FileName);

    private readonly string _filePath;
    private readonly bool _persist;

    /// <summary>创建实例。</summary>
    /// <param name="path">设置文件路径；为空则用 <see cref="DefaultPath"/>。</param>
    public SettingsStore(string? path = null)
        : this(path, persist: true)
    {
    }

    private SettingsStore(string? path, bool persist)
    {
        _filePath = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        _persist = persist;
    }

    /// <summary>
    /// 只在内存里用的设置（不落盘）。测试用，避免碰到真实 <c>%APPDATA%</c>。
    /// </summary>
    public static SettingsStore InMemory() => new(null, persist: false);

    /// <summary>设置文件路径（命名为 FilePath 以免遮蔽 <see cref="System.IO.Path"/>）。</summary>
    public string FilePath => _filePath;

    /// <summary>是否已经存在设置文件。</summary>
    public bool Exists => _persist && File.Exists(_filePath);

    /// <summary>读取设置；任何异常都返回默认值。</summary>
    public AppSettings Load()
    {
        try
        {
            if (!_persist || !File.Exists(_filePath))
            {
                return new AppSettings();
            }

            using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var settings = new AppSettings();

            if (root.TryGetProperty("language", out var language) && language.ValueKind == JsonValueKind.String)
            {
                settings.Language = AppLanguages.FromSettingValue(language.GetString());
            }

            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    /// <summary>写入设置（先写临时文件再替换，避免半截文件）。返回是否成功。</summary>
    public bool Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!_persist)
        {
            // 内存模式：什么都没写，如实返回 false（调用方据此可以判断"没有落盘"）
            return false;
        }

        string? temporary = null;

        try
        {
            var directory = System.IO.Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var buffer = new ArrayBufferWriter<byte>(128);
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", 1);
                writer.WriteString("language", AppLanguages.ToSettingValue(settings.Language));
                writer.WriteEndObject();
            }

            temporary = _filePath + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            File.WriteAllBytes(temporary, buffer.WrittenSpan.ToArray());
            File.Move(temporary, _filePath, overwrite: true);
            temporary = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            if (temporary != null)
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 清理临时文件失败不影响主流程。
                }
            }
        }
    }
}
