// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 系统语言探测：在 InvariantGlobalization 模式下 CultureInfo 拿不到真实区域名，
// 所以这里直接问操作系统（Windows 用 GetUserDefaultUILanguage，类 Unix 看 LANG 等环境变量）。

using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace RePKG.Shared.Localization;

/// <summary>探测操作系统当前的界面语言。</summary>
public static class SystemLanguage
{
    private const int LangChinesePrimary = 0x04;

    /// <summary>探测结果只会是 <see cref="AppLanguage.Chinese"/> 或 <see cref="AppLanguage.English"/>。</summary>
    public static AppLanguage Detect()
    {
        if (OperatingSystem.IsWindows())
        {
            var fromWindows = DetectFromWindows();
            if (fromWindows.HasValue)
            {
                return fromWindows.Value;
            }
        }

        var fromEnvironment = DetectFromEnvironment();
        if (fromEnvironment.HasValue)
        {
            return fromEnvironment.Value;
        }

        return DetectFromCulture();
    }

    /// <summary>把语言名（如 <c>zh-CN</c>）归一成应用语言。</summary>
    public static AppLanguage? FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var value = name.Trim();

        if (value.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return AppLanguage.Chinese;
        }

        if (value.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return AppLanguage.English;
        }

        return null;
    }

    private static AppLanguage? DetectFromWindows()
    {
        try
        {
            var langId = GetUserDefaultUILanguage();
            if (langId == 0)
            {
                return null;
            }

            // LANGID 低 10 位是主语言 ID，中文为 0x04
            var primary = langId & 0x3FF;

            return primary == LangChinesePrimary ? AppLanguage.Chinese : AppLanguage.English;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return null;
        }
    }

    private static AppLanguage? DetectFromEnvironment()
    {
        foreach (var variable in new[] { "LC_ALL", "LC_MESSAGES", "LANG" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var detected = FromName(value.Trim().Trim('"'));
            if (detected.HasValue)
            {
                return detected;
            }

            // 有值但不是中英（如 de_DE.UTF-8）：明确按英文处理，别再往下猜
            return AppLanguage.English;
        }

        return null;
    }

    private static AppLanguage DetectFromCulture()
    {
        var name = CultureInfo.CurrentUICulture.Name;

        return FromName(name) ?? AppLanguage.English;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.None)]
    private static extern ushort GetUserDefaultUILanguage();
}
