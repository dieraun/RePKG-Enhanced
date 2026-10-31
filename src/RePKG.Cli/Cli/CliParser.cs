// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 手写参数解析器（无反射、无第三方依赖、AOT 安全）。
// 兼容原版参数语义：-o/--output、-e/--onlyexts、-i/--ignoreexts、-s/--singledir、
// -r/--recursive、-n/--usename、-c/--copyproject、--no-tex-convert、--overwrite。
// 2.0.0 起错误信息走本地化表，并新增 --lang / --check-update。

using System;
using System.Collections.Generic;
using RePKG.Shared.Catalog;
using RePKG.Shared.Localization;

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
        /// <summary>
        /// 预扫描 <c>--lang</c>：必须在真正解析前拿到语言，
        /// 否则连"未知参数"这样的错误都会用错语言。
        /// </summary>
        public static string? TryPeekLanguage(IReadOnlyList<string> args)
        {
            if (args == null)
            {
                return null;
            }

            for (var i = 0; i < args.Count; i++)
            {
                var arg = args[i];

                if (arg.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring("--lang=".Length);
                }

                if (string.Equals(arg, "--lang", StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1 < args.Count ? args[i + 1] : null;
                }
            }

            return null;
        }

        public static CliOptions Parse(IReadOnlyList<string> args, ILocalizer localizer)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            if (localizer == null) throw new ArgumentNullException(nameof(localizer));

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

                    case "--check-update":
                        options.CheckUpdate = true;
                        break;

                    case "--lang":
                        // 语言已经由 TryPeekLanguage 解析过；这里只要消费掉值，避免被当成位置参数。
                        RequireValue(name, inlineValue, args, ref i, localizer);
                        break;

                    case "-o":
                    case "--output":
                        options.Output = RequireValue(name, inlineValue, args, ref i, localizer);
                        break;

                    case "-e":
                    case "--onlyexts":
                        options.OnlyExtensions = ParseExtensions(RequireValue(name, inlineValue, args, ref i, localizer), name, localizer);
                        break;

                    case "-i":
                    case "--ignoreexts":
                        options.IgnoreExtensions = ParseExtensions(RequireValue(name, inlineValue, args, ref i, localizer), name, localizer);
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
                        options.Concurrency = ParsePositiveInt(name, RequireValue(name, inlineValue, args, ref i, localizer), localizer);
                        break;

                    case "--queue-capacity":
                        options.QueueCapacity = ParsePositiveInt(name, RequireValue(name, inlineValue, args, ref i, localizer), localizer);
                        break;

                    default:
                        throw new CliUsageException(localizer.Format("cli.error.unknownOption", name));
                }
            }

            if (options.CheckUpdate)
            {
                options.Command = CliCommand.Update;
            }

            // 第一个位置参数若是子命令名，则作为命令；否则默认 extract
            if (positionals.Count > 0)
            {
                switch (positionals[0].ToLowerInvariant())
                {
                    case "extract":
                    case "info":
                    case "tex":
                        if (!options.CheckUpdate)
                        {
                            options.Command = positionals[0].ToLowerInvariant() switch
                            {
                                "info" => CliCommand.Info,
                                "tex" => CliCommand.Tex,
                                _ => CliCommand.Extract
                            };
                        }

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

        private static string RequireValue(string name, string? inlineValue, IReadOnlyList<string> args, ref int index, ILocalizer localizer)
        {
            if (inlineValue != null)
            {
                if (inlineValue.Length == 0)
                    throw new CliUsageException(localizer.Format("cli.error.optionNeedsValue", name));

                return inlineValue;
            }

            if (index + 1 >= args.Count)
                throw new CliUsageException(localizer.Format("cli.error.optionNeedsValue", name));

            index++;
            return args[index];
        }

        private static int ParsePositiveInt(string name, string value, ILocalizer localizer)
        {
            if (!int.TryParse(value, out var parsed) || parsed < 1)
                throw new CliUsageException(localizer.Format("cli.error.optionPositiveInt", name));

            return parsed;
        }

        private static string[] ParseExtensions(string value, string optionName, ILocalizer localizer)
        {
            var normalized = PackageEntryFilter.Normalize(new[] { value });

            if (normalized.Length == 0)
                throw new CliUsageException(localizer.Format("cli.error.extListEmpty", optionName));

            return normalized;
        }
    }
}
