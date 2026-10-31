// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 进程入口。任何未处理异常都不能让用户看到一个裸堆栈：统一转成退出码 1。

using System;
using RePKG.Shared.Native;

namespace RePKG.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // 第一件事：把内嵌的原生库自解压并预加载（AOT 单文件发布时 DLL 不在 exe 旁边）
            NativeBootstrap.EnsureLoaded();

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
