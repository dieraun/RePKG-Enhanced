// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
//
// NativeAOT 与 PublishSingleFile 不能共存：一旦 `PublishAot=true`，SDK 会把
// SkiaSharp / HarfBuzzSharp / ANGLE 的原生 DLL 放在 exe 旁边（dotnet/runtime#117986）。
//
// 这里改成「原生库内嵌 + 首启自解压」：
//   * 构建时把原生 DLL 作为 EmbeddedResource 打进 exe（见 RePKG.Cli/RePKG.Gui 的 csproj）；
//   * 启动时最先调用 EnsureLoaded()，把它们解压到 %LOCALAPPDATA%\RePKG-Enhanced\native\<版本>\；
//   * 用 NativeLibrary.Load(绝对路径) 预加载 —— Windows 加载器按文件名匹配已加载模块，
//     因此之后 SkiaSharp/Avalonia 里 DllImport("libSkiaSharp") 之类的裸名加载会直接命中。
//
// 任何一步失败都不抛异常：让正常搜索路径再试一次，最坏情况是“和以前一样需要 DLL 在旁边”。

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace RePKG.Shared.Native
{
    public static class NativeBootstrap
    {
        /// <summary>内嵌资源名的前缀；csproj 里用 <c>RePKG.Native.%(Filename)%(Extension)</c> 生成。</summary>
        public const string ResourcePrefix = "RePKG.Native.";

        private static int _state;
        private static string? _cacheDirectory;
        private static string? _lastError;
        private static readonly List<string> Loaded = new();
        private static readonly List<string> Extracted = new();

        /// <summary>本次实际解压到的目录（没有内嵌资源时为 null）。</summary>
        public static string? CacheDirectory => _cacheDirectory;

        /// <summary>成功加载的原生库绝对路径。</summary>
        public static IReadOnlyList<string> LoadedLibraries => Loaded;

        /// <summary>解压出来的文件（不含「已存在、直接复用」的情况）。</summary>
        public static IReadOnlyList<string> ExtractedFiles => Extracted;

        /// <summary>第一条失败原因（加载失败不致命，仅用于诊断输出）。</summary>
        public static string? LastError => _lastError;

        /// <summary>幂等；必须在任何 SkiaSharp/Avalonia 调用之前执行。</summary>
        public static void EnsureLoaded()
        {
            if (Interlocked.Exchange(ref _state, 1) != 0)
                return;

            try
            {
                Run(Assembly.GetEntryAssembly(), typeof(NativeBootstrap).Assembly);
            }
            catch (Exception ex)
            {
                // 自解压是“锦上添花”：失败也只是退回普通的 DLL 搜索路径
                _lastError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>测试入口：从指定程序集里找 <c>RePKG.Native.*</c> 资源并解压/预加载。</summary>
        internal static void Run(Assembly? entryAssembly, Assembly? sharedAssembly)
        {
            var assemblies = new List<Assembly>(2);

            if (entryAssembly != null)
                assemblies.Add(entryAssembly);

            if (sharedAssembly != null && sharedAssembly != entryAssembly)
                assemblies.Add(sharedAssembly);

            var resources = new Dictionary<string, Stream>(StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in assemblies)
            {
                foreach (var name in assembly.GetManifestResourceNames())
                {
                    if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                        continue;

                    var stream = assembly.GetManifestResourceStream(name);

                    if (stream != null)
                        resources[name.Substring(ResourcePrefix.Length)] = stream;
                }
            }

            if (resources.Count == 0)
                return;

            var appDirectory = AppContext.BaseDirectory;
            var version = GetVersionTag(entryAssembly ?? sharedAssembly);

            foreach (var pair in resources)
            {
                using var stream = pair.Value;
                LoadOne(pair.Key, stream, appDirectory, version);
            }
        }

        private static void LoadOne(string fileName, Stream content, string appDirectory, string version)
        {
            try
            {
                // 已发布成“exe + DLL”的普通形态：直接用现成的，不解压
                var besideExe = Path.Combine(appDirectory, fileName);

                if (File.Exists(besideExe))
                {
                    TryLoad(besideExe);
                    return;
                }

                _cacheDirectory ??= Path.Combine(GetCacheRoot(), version);
                Directory.CreateDirectory(_cacheDirectory);

                var target = Path.Combine(_cacheDirectory, fileName);

                if (!File.Exists(target) || new FileInfo(target).Length != content.Length)
                {
                    // 先写临时文件再改名：并发启动时不会读到半个 DLL
                    var temp = target + "." + Environment.ProcessId.ToString("x") + ".tmp";

                    using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                        content.CopyTo(file);

                    File.Move(temp, target, overwrite: true);
                    Extracted.Add(target);
                }

                TryLoad(target);
            }
            catch (Exception ex)
            {
                _lastError ??= fileName + ": " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static void TryLoad(string path)
        {
            try
            {
                var handle = NativeLibrary.Load(path);

                if (handle != IntPtr.Zero)
                    Loaded.Add(path);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is BadImageFormatException || ex is IOException)
            {
                _lastError ??= path + ": " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static string GetCacheRoot()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrEmpty(local))
                local = Path.GetTempPath();

            return Path.Combine(local, "RePKG-Enhanced", "native");
        }

        private static string GetVersionTag(Assembly? assembly)
        {
            var raw = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (string.IsNullOrWhiteSpace(raw))
                raw = assembly?.GetName().Version?.ToString() ?? "0";

            // 版本号会进目录名：只留字母数字与点
            var builder = new System.Text.StringBuilder(raw.Length);

            foreach (var c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '.')
                    builder.Append(c);
                else
                    break;
            }

            return builder.Length == 0 ? "0" : builder.ToString();
        }
    }
}
