// 基于 notscuffed/RePKG (MIT) 二次修改：本文件为 RePKG-Enhanced 新增，不来自原版。
//
// 语言枚举：System 表示「跟随系统」，其余为显式选择。

using System;
using System.Globalization;

namespace RePKG.Shared.Localization;

/// <summary>应用界面语言。</summary>
public enum AppLanguage
{
    /// <summary>跟随操作系统/当前区域设置。</summary>
    System = 0,

    /// <summary>简体中文。</summary>
    Chinese = 1,

    /// <summary>English.</summary>
    English = 2,
}

/// <summary>语言解析与展示名。</summary>
public static class AppLanguages
{
    /// <summary>配置文件里使用的稳定标识（与枚举名无关，避免将来改枚举改名破坏用户配置）。</summary>
    public static string ToSettingValue(AppLanguage language) => language switch
    {
        AppLanguage.Chinese => "zh",
        AppLanguage.English => "en",
        _ => "system",
    };

    /// <summary>把配置值解析回枚举；无法识别时回退 <see cref="AppLanguage.System"/>。</summary>
    public static AppLanguage FromSettingValue(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "zh" or "zh-cn" or "zh-hans" or "chinese" or "中文" => AppLanguage.Chinese,
        "en" or "en-us" or "en-gb" or "english" => AppLanguage.English,
        _ => AppLanguage.System,
    };

    /// <summary>命令行 `--lang` 的解析：只接受显式值，`system`/`auto` 也映射为跟随系统。</summary>
    public static bool TryParse(string? value, out AppLanguage language)
    {
        language = FromSettingValue(value);
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "system" or "auto" or "zh" or "zh-cn" or "zh-hans" or "chinese" or "中文"
            or "en" or "en-us" or "en-gb" or "english";
    }

    /// <summary>该语言在语言选择器里的展示名（用自身语言写，避免"看不懂自己选的项"）。</summary>
    public static string DisplayName(AppLanguage language) => language switch
    {
        AppLanguage.Chinese => "简体中文",
        AppLanguage.English => "English",
        _ => "跟随系统 / System",
    };

    /// <summary>
    /// 把「跟随系统」解析成具体语言。
    /// 优先用调用方给的 <paramref name="uiCulture"/>；否则先看当前 UI culture，
    /// 拿不到区域名时（InvariantGlobalization / 精简运行时）再问操作系统。
    /// </summary>
    public static AppLanguage Resolve(AppLanguage language, CultureInfo? uiCulture = null)
    {
        if (language != AppLanguage.System)
        {
            return language;
        }

        if (uiCulture is not null)
        {
            return IsChinese(uiCulture) ? AppLanguage.Chinese : AppLanguage.English;
        }

        var current = CultureInfo.CurrentUICulture;
        if (!string.IsNullOrEmpty(current.Name) && current.Name != "iv")
        {
            if (IsChinese(current))
            {
                return AppLanguage.Chinese;
            }

            if (!string.IsNullOrEmpty(CultureInfo.CurrentCulture.Name))
            {
                return IsChinese(CultureInfo.CurrentCulture) ? AppLanguage.Chinese : AppLanguage.English;
            }
        }

        // 区域名不可用：直接问操作系统
        return SystemLanguage.Detect();
    }

    /// <summary>该区域是否属于中文。</summary>
    public static bool IsChinese(CultureInfo? culture)
    {
        if (culture is null)
        {
            return false;
        }

        var name = culture.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 无区域信息的自定义 CultureInfo（例如某些精简环境）只有 TwoLetterISOLanguageName。
        return string.Equals(culture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase);
    }
}
