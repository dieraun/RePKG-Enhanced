// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// CLI 入口分发：解析参数、选择命令、把 Ctrl+C 翻译成取消信号。
// 真正的导出逻辑全在 RePKG.Shared 的 ExportQueue 里，这里不碰条目循环。
// 2.0.0：语言按 --lang → REPKG_LANG → settings.json → 系统 解析，文案全部走本地化表。

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RePKG.Cli.Commands;
using RePKG.Shared;
using RePKG.Shared.Localization;
using RePKG.Shared.Settings;

namespace RePKG.Cli
{
    public static class CliApp
    {
        public static int Run(string[] args, TextWriter output, TextWriter error)
            => Run(args, output, error, null);

        /// <summary>供测试注入设置文件路径的入口。</summary>
        public static int Run(string[] args, TextWriter output, TextWriter error, SettingsStore? settings)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (error == null) throw new ArgumentNullException(nameof(error));

            var store = settings ?? new SettingsStore();

            // 语言必须在解析前确定：否则连"未知参数"都会用错语言。
            var language = LanguageResolver.Resolve(CliParser.TryPeekLanguage(args), store);
            var localizer = new Localizer(language: language);

            CliOptions options;

            try
            {
                options = CliParser.Parse(args, localizer);
            }
            catch (CliUsageException ex)
            {
                WriteUsageError(error, localizer, ex.Message);
                return (int) ExitCode.UsageError;
            }

            switch (options.Command)
            {
                case CliCommand.Help:
                    output.Write(HelpText.Render(localizer));
                    return (int) ExitCode.Success;

                case CliCommand.Version:
                    output.WriteLine($"repkg {AppInfo.Version}");
                    return (int) ExitCode.Success;
            }

            if (options.Command != CliCommand.Update && options.Inputs.Count == 0)
            {
                WriteUsageError(error, localizer, localizer.Get("cli.error.missingInput"));
                return (int) ExitCode.UsageError;
            }

            using (var cancellation = new CancellationTokenSource())
            {
                ConsoleCancelEventHandler handler = (sender, eventArgs) =>
                {
                    // 第一次 Ctrl+C：请求在条目边界取消；再按一次由运行时终止
                    eventArgs.Cancel = true;
                    cancellation.Cancel();
                };

                Console.CancelKeyPress += handler;

                try
                {
                    switch (options.Command)
                    {
                        case CliCommand.Info:
                            return (int) InfoCommand.Run(options, output, localizer);

                        case CliCommand.Tex:
                            return (int) TexCommand.Run(options, output, cancellation.Token, localizer);

                        case CliCommand.Update:
                            return (int) UpdateCommand.RunAsync(output, error, localizer, cancellation.Token)
                                .GetAwaiter()
                                .GetResult();

                        default:
                            return (int) ExtractCommand.RunAsync(options, output, cancellation.Token, localizer)
                                .GetAwaiter()
                                .GetResult();
                    }
                }
                catch (CliUsageException ex)
                {
                    WriteUsageError(error, localizer, ex.Message);
                    return (int) ExitCode.UsageError;
                }
                catch (OperationCanceledException)
                {
                    error.WriteLine(localizer.Get("cli.canceled"));
                    return (int) ExitCode.Canceled;
                }
                finally
                {
                    Console.CancelKeyPress -= handler;
                }
            }
        }

        private static void WriteUsageError(TextWriter error, ILocalizer localizer, string message)
        {
            error.WriteLine(localizer.Format("cli.error.prefix", message));
            error.WriteLine(localizer.Get("cli.error.hint"));
        }
    }
}
