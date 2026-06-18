using System;
using Avalonia.Markup.Xaml;

namespace Histshot.Localization;

/// <summary>
/// XAML markup extension that resolves a localized string by key, e.g.
/// <c>Text="{l:Localize PrimaryHotkey}"</c>. Resolved once when the XAML is loaded,
/// using the current <see cref="Localization.Language"/>.
/// </summary>
public class LocalizeExtension : MarkupExtension
{
    public LocalizeExtension() { }

    public LocalizeExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Localization.Get(Key);
}
