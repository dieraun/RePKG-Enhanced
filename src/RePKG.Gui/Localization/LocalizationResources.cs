// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 把 RePKG.Shared 的本地化文案表挂到 Avalonia 的 Application.Resources 上，
// 这样 XAML 里的 {DynamicResource gui.addPackage} 之类的写法可以在运行时换语言即时生效。
//
// 注意两点：
// 1) Avalonia 的 DynamicResource 只在资源字典**发生变化**时才重新求值，所以换语言要
//    把整本字典从 MergedDictionaries 摘下来、重灌、再插回去（集合变更会触发 ResourcesChanged）。
// 2) 同一个 ResourceDictionary 实例不能同时挂到两个 Application 上（无头测试每题一个
//    Application 会踩到 "already has a parent"），所以换宿主时必须新建一个实例。

using System.Collections.Generic;
using Avalonia.Controls;
using RePKG.Shared.Localization;

namespace RePKG.Gui.Localization
{
    internal static class LocalizationResources
    {
        private static ResourceDictionary? _bundle;
        private static Avalonia.Application? _owner;

        public static void Apply(Avalonia.Application application, ILocalizer localizer)
        {
            var values = localizer.Catalog.For(localizer.Resolved);

            if (_bundle is null || !ReferenceEquals(_owner, application))
            {
                if (_bundle is not null && _owner is not null)
                {
                    _owner.Resources.MergedDictionaries.Remove(_bundle);
                }

                _bundle = new ResourceDictionary();
                _owner = application;
            }
            else
            {
                application.Resources.MergedDictionaries.Remove(_bundle);
            }

            _bundle.Clear();

            foreach (var pair in values)
                _bundle[pair.Key] = pair.Value;

            application.Resources.MergedDictionaries.Insert(0, _bundle);
        }

        /// <summary>供测试断言：当前挂上去的键值数量。</summary>
        internal static int Count => _bundle?.Count ?? 0;

        internal static IReadOnlyDictionary<string, object?> Snapshot()
        {
            var copy = new Dictionary<string, object?>(Count, System.StringComparer.Ordinal);

            if (_bundle is null)
                return copy;

            foreach (var pair in _bundle)
            {
                if (pair.Key is string key)
                    copy[key] = pair.Value;
            }

            return copy;
        }
    }
}
