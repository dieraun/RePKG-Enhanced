// 基于 notscuffed/RePKG (MIT) 二次修改：本文件为 RePKG-Enhanced 新增，不来自原版。

using System;
using System.Globalization;
using RePKG.Shared.Settings;

namespace RePKG.Shared.Localization;

/// <summary>
/// 语言优先级：命令行 <c>--lang</c> → 环境变量 <c>REPKG_LANG</c> → settings.json → 系统 UI culture。
/// </summary>
public static class LanguageResolver
{
    /// <summary>环境变量名（便于脚本/CI 覆盖）。</summary>
    public const string EnvironmentVariable = "REPKG_LANG";

    /// <summary>解析最终语言。</summary>
    /// <param name="commandLineValue">命令行传入的值（可为空）。</param>
    /// <param name="store">设置存储（可为空）。</param>
    /// <param name="environmentValue">环境变量值；为空则读 <see cref="EnvironmentVariable"/>。</param>
    /// <param name="uiCulture">系统文化；为空则用当前 UI culture。</param>
    public static AppLanguage Resolve(
        string? commandLineValue,
        SettingsStore? store = null,
        string? environmentValue = null,
        CultureInfo? uiCulture = null)
    {
        if (AppLanguages.TryParse(commandLineValue, out var fromCommandLine))
        {
            return fromCommandLine;
        }

        var env = environmentValue ?? Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (AppLanguages.TryParse(env, out var fromEnvironment))
        {
            return fromEnvironment;
        }

        if (store != null)
        {
            var settings = store.Load();

            if (settings.Language != AppLanguage.System)
            {
                return settings.Language;
            }
        }

        return AppLanguages.Resolve(AppLanguage.System, uiCulture);
    }
}
