// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 手写参数解析器（无反射、无第三方依赖、AOT 安全）。
// 兼容原版参数语义：-o/--output、-e/--onlyexts、-i/--ignoreexts、-s/--singledir、
// -r/--recursive、-n/--usename、-c/--copyproject、--no-tex-convert、--overwrite。

using System;
using System.Collections.Generic;
using RePKG.Shared.Catalog;

namespace RePKG.Cli
{
    /// <summary>参数错误：由 Program 转成退出码 2。</summary>
    public sealed class CliUsageException : Exception
    {
        public CliUsageException(string message) : base(message)
        {
        }
    }

    public static class CliParser
    {
        public static CliOptions Parse(IReadOnlyList<string> args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));

            var options = new CliOptions();
            var positionals = new List<string>();

            for (var i = 0; i < args.Count; i++)
            {
                var arg = args[i];

                if (arg == "--")
                {
                    for (i++; i < args.Count; i++) positionals.Add(args[i]);

                    break;
                }

                if (arg.Length == 0 || arg[0] != '-' || arg == "-")
                {
                    positionals.Add(arg);
                    continue;
                }

                var (name, inlineValue) = SplitName(arg);

                switch (name)
                {
                    case "-h":
                    case "--help":
                        options.Command = CliCommand.Help;
                        return options;

                    case "--version":
                        options.Command = CliCommand.Version;
                        return options;

                    case "-o":
                    case "--output":
                        options.Output = RequireValue(name, inlineValue, args, ref i);
                        break;

                    case "-e":
                    case "--onlyexts":
                        options.OnlyExtensions = ParseExtensions(RequireValue(name, inlineValue, args, ref i));
                        break;

                    case "-i":
                    case "--ignoreexts":
                        options.IgnoreExtensions = ParseExtensions(RequireValue(name, inlineValue, args, ref i));
                        break;

                    case "-s":
                    case "--singledir":
                        options.SingleDirectory = true;
                        break;

                    case "-r":
                    case "--recursive":
                        options.Recursive = true;
                        break;

                    case "-n":
                    case "--usename":
                        options.UseName = true;
                        break;

                    case "-c":
                    case "--copyproject":
                        options.CopyProject = true;
                        break;

                    case "--no-tex-convert":
                        options.NoTexConvert = true;
                        break;

                    case "--overwrite":
                        options.Overwrite = true;
                        break;

                    case "--no-log":
                        options.NoLog = true;
                        break;

                    case "--manifest":
                        options.Manifest = true;
                        break;

                    case "--json":
                        options.Json = true;
                        break;

                    case "--verbose":
                        options.Verbose = true;
                        break;

                    case "--concurrency":
                        options.Concurrency = ParsePositiveInt(name, RequireValue(name, inlineValue, args, ref i));
                        break;

                    case "--queue-capacity":
                        options.QueueCapacity = ParsePositiveInt(name, RequireValue(name, inlineValue, args, ref i));
                        break;

                    default:
                        throw new CliUsageException($"未知参数：{name}");
                }
            }

            // 第一个位置参数若是子命令名，则作为命令；否则默认 extract
            if (positionals.Count > 0)
            {
                switch (positionals[0].ToLowerInvariant())
                {
                    case "extract":
                    case "info":
                    case "tex":
                        options.Command = positionals[0].ToLowerInvariant() switch
                        {
                            "info" => CliCommand.Info,
                            "tex" => CliCommand.Tex,
                            _ => CliCommand.Extract
                        };

                        positionals.RemoveAt(0);
                        break;
                }
            }

            options.Inputs.AddRange(positionals);

            return options;
        }

        private static (string Name, string? Value) SplitName(string arg)
        {
            var equals = arg.IndexOf('=');

            if (equals < 0)
                return (arg, null);

            return (arg.Substring(0, equals), arg.Substring(equals + 1));
        }

        private static string RequireValue(string name, string? inlineValue, IReadOnlyList<string> args, ref int index)
        {
            if (inlineValue != null)
            {
                if (inlineValue.Length == 0)
                    throw new CliUsageException($"参数 {name} 需要一个值");

                return inlineValue;
            }

            if (index + 1 >= args.Count)
                throw new CliUsageException($"参数 {name} 需要一个值");

            index++;
            return args[index];
        }

        private static int ParsePositiveInt(string name, string value)
        {
            if (!int.TryParse(value, out var parsed) || parsed < 1)
                throw new CliUsageException($"参数 {name} 需要正整数，收到：{value}");

            return parsed;
        }

        private static string[] ParseExtensions(string value)
        {
            var normalized = PackageEntryFilter.Normalize(new[] { value });

            if (normalized.Length == 0)
                throw new CliUsageException($"扩展名列表为空：{value}");

            return normalized;
        }
    }
}
