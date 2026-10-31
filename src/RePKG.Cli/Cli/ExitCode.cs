// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 退出码是对脚本调用方的契约：0 全成功、1 有失败项、2 用法错误、130 用户取消。

namespace RePKG.Cli
{
    public enum ExitCode
    {
        /// <summary>全部成功。</summary>
        Success = 0,

        /// <summary>有失败项（条目失败或包打不开），其余条目已完成。</summary>
        PartialFailure = 1,

        /// <summary>命令行参数错误。</summary>
        UsageError = 2,

        /// <summary>用户中断（Ctrl+C）。</summary>
        Canceled = 130
    }
}
