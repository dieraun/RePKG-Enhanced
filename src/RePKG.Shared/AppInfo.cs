// 基于 notscuffed/RePKG (MIT) 二次修改：本文件为 RePKG-Enhanced 新增，不来自原版。

using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RePKG.Shared;

/// <summary>应用元信息：版本、作者、仓库地址、运行环境。关于窗口与 CLI 共用。</summary>
public static class AppInfo
{
    /// <summary>产品名。</summary>
    public const string ProductName = "RePKG-Enhanced";

    /// <summary>作者。</summary>
    public const string Author = "dieraun";

    /// <summary>GitHub 仓库地址。</summary>
    public const string RepositoryUrl = "https://github.com/dieraun/RePKG-Enhanced";

    /// <summary>最新 Release 的人读页面。</summary>
    public const string LatestReleaseUrl = RepositoryUrl + "/releases/latest";

    /// <summary>最新 Release 的 GitHub API（更新检查用）。</summary>
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/dieraun/RePKG-Enhanced/releases/latest";

    /// <summary>许可证名称。</summary>
    public const string LicenseName = "MIT";

    /// <summary>语义化版本号（不含构建元数据），例如 <c>2.0.0</c>。</summary>
    public static string Version => ParseVersion(InformationalVersion);

    /// <summary>完整版本号（含 SDK 追加的源码版本信息），已去掉 <c>+</c> 之后的部分。</summary>
    public static string InformationalVersion
    {
        get
        {
            var assembly = typeof(AppInfo).Assembly;
            var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (string.IsNullOrWhiteSpace(info))
            {
                info = assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            }

            var plus = info.IndexOf('+');
            return plus >= 0 ? info.Substring(0, plus) : info;
        }
    }

    /// <summary>运行环境描述，例如 <c>.NET 10.0.12 · win-x64</c>。</summary>
    public static string RuntimeDescription
    {
        get
        {
            var framework = RuntimeInformation.FrameworkDescription;
            var rid = RuntimeInformation.RuntimeIdentifier;
            return string.IsNullOrEmpty(rid) ? framework : $"{framework} · {rid}";
        }
    }

    /// <summary>操作系统描述。</summary>
    public static string OperatingSystem => RuntimeInformation.OSDescription.Trim();

    /// <summary>架构描述。</summary>
    public static string Architecture => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    /// <summary>把 <c>v2.0.0</c> / <c>2.0.0+abc</c> 归一化成 <c>2.0.0</c>。</summary>
    public static string ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "0.0.0";
        }

        var value = text.Trim();

        if (value.Length > 0 && (value[0] == 'v' || value[0] == 'V'))
        {
            value = value.Substring(1);
        }

        var cut = value.IndexOfAny(new[] { '-', '+' });
        if (cut > 0)
        {
            value = value.Substring(0, cut);
        }

        return value;
    }
}
