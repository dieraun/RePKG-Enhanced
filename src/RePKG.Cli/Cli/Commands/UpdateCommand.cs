// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced 2.0.0）
// `repkg --check-update`：唯一会联网的路径，且必须由用户显式触发。

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RePKG.Shared.Localization;
using RePKG.Shared.Updates;

namespace RePKG.Cli.Commands
{
    internal static class UpdateCommand
    {
        /// <summary>检查 GitHub 上的最新 release。</summary>
        /// <returns>0 表示检查完成，1 表示检查失败（网络/解析）。</returns>
        internal static async Task<ExitCode> RunAsync(
            TextWriter output,
            TextWriter error,
            ILocalizer localizer,
            CancellationToken cancellationToken)
        {
            error.WriteLine(localizer.Get("cli.update.checking"));

            using var checker = new UpdateChecker();
            var result = await checker.CheckAsync(Shared.AppInfo.Version, cancellationToken).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                error.WriteLine(localizer.Format("cli.update.failed", result.Error ?? "unknown error"));
                return ExitCode.PartialFailure;
            }

            if (!result.IsNewer)
            {
                output.WriteLine(localizer.Format("cli.update.upToDate", result.CurrentVersion));
                return ExitCode.Success;
            }

            output.WriteLine(localizer.Format(
                "cli.update.available",
                result.LatestVersion ?? "?",
                result.CurrentVersion,
                result.ReleaseUrl ?? Shared.AppInfo.LatestReleaseUrl));

            return ExitCode.Success;
        }
    }
}
