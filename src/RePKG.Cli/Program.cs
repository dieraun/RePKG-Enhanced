// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 进程入口。任何未处理异常都不能让用户看到一个裸堆栈：统一转成退出码 1。

using System;

namespace RePKG.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                return CliApp.Run(args, Console.Out, Console.Error);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"未处理的异常：{ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);

                return (int) ExitCode.PartialFailure;
            }
        }
    }
}
