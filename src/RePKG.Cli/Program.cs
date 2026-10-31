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
            // 控制台可能不是 UTF-8（中文 Windows 默认 GBK），中文/箭头会变成乱码。
            // 只在实际连着终端时改，重定向给脚本时保持原样。
            try
            {
                if (!Console.IsOutputRedirected)
                    Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }
            catch (Exception)
            {
                // 某些宿主（无控制台的服务）不允许改编码，忽略
            }

            // 第一件事：把内嵌的原生库自解压并预加载（AOT 单文件发布时 DLL 不在 exe 旁边）
            NativeBootstrap.EnsureLoaded();

            if (NativeBootstrap.LoadedLibraries.Count == 0 && NativeBootstrap.LastError != null)
            {
                Console.Error.WriteLine("[warn] native libraries were not loaded: " + NativeBootstrap.LastError);
                Console.Error.WriteLine("[warn] set REPKG_NATIVE_CACHE to a writable directory if %LOCALAPPDATA% is locked down");
            }

            if (Environment.GetEnvironmentVariable("REPKG_NATIVE_DEBUG") == "1")
            {
                foreach (var line in NativeBootstrap.Describe())
                    Console.Error.WriteLine("[native] " + line);
            }

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
