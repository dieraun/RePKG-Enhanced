// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 命令行参数模型。刻意不复用原版的 CommandLineParser（反射式，NativeAOT 不友好），
// 也不保留 interactive 子模式（无法在裁剪/单文件环境里可靠工作）。

using System.Collections.Generic;

namespace RePKG.Cli
{
    public enum CliCommand
    {
        Extract,
        Info,
        Tex,
        Help,
        Version
    }

    /// <summary>解析后的命令行参数。</summary>
    public sealed class CliOptions
    {
        public CliCommand Command { get; set; } = CliCommand.Extract;

        /// <summary>位置参数：extract/info 为 pkg 文件或目录；tex 为 .tex 文件或目录。</summary>
        public List<string> Inputs { get; } = new List<string>();

        public string? Output { get; set; }

        public IReadOnlyCollection<string> OnlyExtensions { get; set; } = System.Array.Empty<string>();

        public IReadOnlyCollection<string> IgnoreExtensions { get; set; } = System.Array.Empty<string>();

        /// <summary>不保留 pkg 内的目录结构。</summary>
        public bool SingleDirectory { get; set; }

        /// <summary>目录输入时递归查找。</summary>
        public bool Recursive { get; set; }

        /// <summary>用 project.json 里的 title 作为输出子目录名。</summary>
        public bool UseName { get; set; }

        /// <summary>把 project.json 一并写出。</summary>
        public bool CopyProject { get; set; }

        /// <summary>不做 TEX 转换，原样导出 .tex。</summary>
        public bool NoTexConvert { get; set; }

        public bool Overwrite { get; set; }

        public bool NoLog { get; set; }

        public bool Json { get; set; }

        public int? Concurrency { get; set; }

        public int? QueueCapacity { get; set; }

        public bool Verbose { get; set; }
    }
}
