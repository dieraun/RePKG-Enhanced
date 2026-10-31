// 基于 notscuffed/RePKG (MIT) 二次修改：本文件为 RePKG-Enhanced 新增，不来自原版。

using System;
using System.Globalization;

namespace RePKG.Shared.Localization;

/// <summary>提供当前语言的文案查表能力，并支持运行时切换。</summary>
public interface ILocalizer
{
    /// <summary>用户选择（可能是 <see cref="AppLanguage.System"/>）。</summary>
    AppLanguage Setting { get; }

    /// <summary>实际生效的语言（只会是中文或英文）。</summary>
    AppLanguage Resolved { get; }

    /// <summary>当前是否为中文。</summary>
    bool IsChinese { get; }

    /// <summary>底层文案表（GUI 需要把它整本挂成 Avalonia 资源字典）。</summary>
    LocalizationCatalog Catalog { get; }

    /// <summary>切换语言后触发（GUI 用它刷新动态资源）。</summary>
    event EventHandler? LanguageChanged;

    /// <summary>切换语言；<see cref="AppLanguage.System"/> 表示重新跟随系统。</summary>
    void SetLanguage(AppLanguage language);

    /// <summary>按键取文案；键不存在时返回键本身，便于暴露漏配。</summary>
    string Get(string key);

    /// <summary>按键取文案并做 <see cref="string.Format(IFormatProvider, string, object?[])"/>。</summary>
    string Format(string key, params object?[] args);
}

/// <summary>默认实现：内置字典 + 运行时切语言。</summary>
public sealed class Localizer : ILocalizer
{
    private readonly LocalizationCatalog _catalog;
    private readonly object _gate = new();

    private AppLanguage _setting;
    private AppLanguage _resolved;

    /// <summary>创建实例。</summary>
    /// <param name="catalog">文案表；为空则使用 <see cref="LocalizationCatalog.CreateDefault"/>。</param>
    /// <param name="language">初始语言（可为 System）。</param>
    /// <param name="uiCulture">用于解析 System 的文化；默认取当前 UI culture。</param>
    public Localizer(LocalizationCatalog? catalog = null, AppLanguage language = AppLanguage.System, CultureInfo? uiCulture = null)
    {
        _catalog = catalog ?? LocalizationCatalog.CreateDefault();
        _setting = language;
        _resolved = AppLanguages.Resolve(language, uiCulture);
    }

    /// <inheritdoc />
    public AppLanguage Setting
    {
        get { lock (_gate) { return _setting; } }
    }

    /// <inheritdoc />
    public AppLanguage Resolved
    {
        get { lock (_gate) { return _resolved; } }
    }

    /// <inheritdoc />
    public bool IsChinese => Resolved == AppLanguage.Chinese;

    /// <inheritdoc />
    public event EventHandler? LanguageChanged;

    /// <summary>内置文案表（测试与资源构建共用）。</summary>
    public LocalizationCatalog Catalog => _catalog;

    /// <inheritdoc />
    public void SetLanguage(AppLanguage language)
    {
        AppLanguage previous;

        lock (_gate)
        {
            previous = _resolved;
            _setting = language;
            _resolved = AppLanguages.Resolve(language);
        }

        if (previous != Resolved)
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>按显式文化重新解析 System（用于"跟随系统"时感知系统变化）。</summary>
    public void SetCulture(CultureInfo culture)
    {
        AppLanguage previous;

        lock (_gate)
        {
            if (_setting != AppLanguage.System)
            {
                return;
            }

            previous = _resolved;
            _resolved = AppLanguages.Resolve(AppLanguage.System, culture);
        }

        if (previous != Resolved)
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var table = _catalog.For(Resolved);
        return table.TryGetValue(key, out var value) ? value : key;
    }

    /// <inheritdoc />
    public string Format(string key, params object?[] args)
    {
        var template = Get(key);

        if (args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            // 文案占位符与参数不匹配时不要把异常抛给用户界面。
            return template;
        }
    }

    /// <summary>索引器，便于在绑定/模板里写 <c>Localizer["gui.start"]</c>。</summary>
    public string this[string key] => Get(key);
}
