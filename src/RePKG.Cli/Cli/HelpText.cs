// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 帮助文本：2.0.0 起由本地化表提供中英两份（见 RePKG.Shared/Localization/CatalogCli.cs）。

using RePKG.Shared;
using RePKG.Shared.Localization;

namespace RePKG.Cli
{
    public static class HelpText
    {
        /// <summary>当前版本号（与 AssemblyInformationalVersion 保持一致）。</summary>
        public static string Version => AppInfo.Version;

        /// <summary>渲染当前语言的帮助文本。</summary>
        public static string Render(ILocalizer localizer)
            => localizer.Format("cli.help", AppInfo.Version);
    }
}
