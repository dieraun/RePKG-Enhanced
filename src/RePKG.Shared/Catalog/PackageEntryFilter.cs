using System;
using System.Collections.Generic;
using RePKG.Core.Package;

namespace RePKG.Shared.Catalog
{
    /// <summary>
    /// 扩展名过滤，语义对齐原版 <c>RePKG\Command\Extract.cs</c>：
    /// <list type="bullet">
    /// <item>扩展名大小写不敏感，比较用 <see cref="StringComparison.OrdinalIgnoreCase"/></item>
    /// <item>调用方可以写 <c>tex</c> 或 <c>.tex</c>，自动补点</item>
    /// <item><c>ignore</c> 优先于 <c>only</c></item>
    /// <item>匹配方式是 <c>FullPath.EndsWith(ext)</c>，与原版一致（因此 <c>.png</c> 也匹配 <c>icon.png</c>）</item>
    /// </list>
    /// </summary>
    public sealed class PackageEntryFilter
    {
        private readonly string[] _only;
        private readonly string[] _ignore;

        public PackageEntryFilter(IEnumerable<string>? onlyExtensions, IEnumerable<string>? ignoreExtensions)
        {
            _only = Normalize(onlyExtensions);
            _ignore = Normalize(ignoreExtensions);
        }

        public static readonly PackageEntryFilter None = new PackageEntryFilter(null, null);

        public IReadOnlyList<string> OnlyExtensions => _only;
        public IReadOnlyList<string> IgnoreExtensions => _ignore;

        /// <summary>是否应导出该条目。</summary>
        public bool Matches(PackageEntry entry)
        {
            var path = entry.FullPath ?? string.Empty;

            foreach (var ext in _ignore)
            {
                if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (_only.Length == 0)
                return true;

            foreach (var ext in _only)
            {
                if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>把 <c>tex,png</c>/<c>.tex,.png</c> 统一成 <c>.tex</c>/<c>.png</c>。</summary>
        public static string[] Normalize(IEnumerable<string>? extensions)
        {
            if (extensions == null)
                return Array.Empty<string>();

            var list = new List<string>();

            foreach (var item in extensions)
            {
                if (string.IsNullOrWhiteSpace(item))
                    continue;

                foreach (var piece in item.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var ext = piece.Trim();

                    if (ext.Length == 0)
                        continue;

                    if (ext[0] != '.')
                        ext = "." + ext;

                    list.Add(ext);
                }
            }

            return list.ToArray();
        }
    }
}
