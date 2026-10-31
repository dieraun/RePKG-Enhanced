// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// CLI 入口分发：解析参数、选择命令、把 Ctrl+C 翻译成取消信号。
// 真正的导出逻辑全在 RePKG.Shared 的 ExportQueue 里，这里不碰条目循环。

using System;
using System.IO;
using System.Threading;

namespace RePKG.Cli
{
    public static class CliApp
    {
        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (error == null) throw new ArgumentNullException(nameof(error));

            CliOptions options;

            try
            {
                options = CliParser.Parse(args);
            }
            catch (CliUsageException ex)
            {
                error.WriteLine($"参数错误：{ex.Message}");
                error.WriteLine("使用 `repkg --help` 查看用法。");

                return (int) ExitCode.UsageError;
            }

            switch (options.Command)
            {
                case CliCommand.Help:
                    output.Write(HelpText.Usage);
                    return (int) ExitCode.Success;

                case CliCommand.Version:
                    output.WriteLine($"repkg {HelpText.Version}");
                    return (int) ExitCode.Success;
            }

            try
            {
                if (options.Inputs.Count == 0)
                    throw new CliUsageException("缺少输入路径");
            }
            catch (CliUsageException ex)
            {
                error.WriteLine($"参数错误：{ex.Message}");
                error.WriteLine("使用 `repkg --help` 查看用法。");

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
                            return (int) InfoCommand.Run(options, output);

                        case CliCommand.Tex:
                            return (int) TexCommand.Run(options, output, cancellation.Token);

                        default:
                            return (int) ExtractCommand.RunAsync(options, output, cancellation.Token)
                                .GetAwaiter()
                                .GetResult();
                    }
                }
                catch (CliUsageException ex)
                {
                    error.WriteLine($"参数错误：{ex.Message}");
                    error.WriteLine("使用 `repkg --help` 查看用法。");

                    return (int) ExitCode.UsageError;
                }
                catch (OperationCanceledException)
                {
                    error.WriteLine("已取消。");

                    return (int) ExitCode.Canceled;
                }
                finally
                {
                    Console.CancelKeyPress -= handler;
                }
            }
        }
    }
}
