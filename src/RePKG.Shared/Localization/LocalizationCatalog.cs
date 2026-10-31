// 基于 notscuffed/RePKG (MIT) 二次修改：本文件为 RePKG-Enhanced 新增，不来自原版。
//
// 本地化字典的骨架：一条 `Add(key, zh, en)` 同时写入中英两份表，
// 因此两种语言**不可能**出现键缺失（构建期唯一入口），测试只需校验非空与占位符一致。

using System;
using System.Collections.Generic;
using System.Globalization;

namespace RePKG.Shared.Localization;

/// <summary>中英双语字符串表。</summary>
public sealed class LocalizationCatalog
{
    private readonly Dictionary<string, string> _chinese = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _english = new(StringComparer.Ordinal);

    /// <summary>中文表（只读视图）。</summary>
    public IReadOnlyDictionary<string, string> Chinese => _chinese;

    /// <summary>英文表（只读视图）。</summary>
    public IReadOnlyDictionary<string, string> English => _english;

    /// <summary>键数量。</summary>
    public int Count => _chinese.Count;

    /// <summary>同时登记中英两条文案；键重复直接抛错（防止静默覆盖）。</summary>
    public LocalizationCatalog Add(string key, string chinese, string english)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_chinese.TryAdd(key, chinese))
        {
            throw new InvalidOperationException($"Duplicate localization key: {key}");
        }

        _english.Add(key, english);
        return this;
    }

    /// <summary>取某个语言的表。</summary>
    public IReadOnlyDictionary<string, string> For(AppLanguage language)
        => language == AppLanguage.Chinese ? _chinese : _english;

    /// <summary>构建内置的全部文案（英文为正体，中文为对照）。</summary>
    public static LocalizationCatalog CreateDefault()
    {
        var catalog = new LocalizationCatalog();
        CatalogApp.Register(catalog);
        CatalogGui.Register(catalog);
        CatalogCli.Register(catalog);
        return catalog;
    }
}

/// <summary>占位符校验辅助（测试与自检共用）。</summary>
public static class LocalizationFormat
{
    /// <summary>取出形如 <c>{0}</c> / <c>{1:0.0}</c> 的索引，用于断言两语言占位符一致。</summary>
    public static int[] PlaceholderIndexes(string template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return Array.Empty<int>();
        }

        var result = new List<int>();

        for (var i = 0; i < template.Length - 1; i++)
        {
            if (template[i] != '{' || template[i + 1] == '{')
            {
                continue;
            }

            var end = template.IndexOf('}', i + 1);
            if (end < 0)
            {
                break;
            }

            var inner = template.Substring(i + 1, end - i - 1);
            var colon = inner.IndexOf(':');
            if (colon >= 0)
            {
                inner = inner.Substring(0, colon);
            }

            if (int.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                result.Add(index);
            }

            i = end;
        }

        result.Sort();
        return result.ToArray();
    }
}
