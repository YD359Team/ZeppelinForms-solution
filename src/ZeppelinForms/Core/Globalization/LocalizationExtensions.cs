using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Core.Globalization;

/// <summary>A key assigned to one property of one element.</summary>
internal sealed class LocalizedBinding(string property, Func<string> resolve, Action<UIElement, string> apply)
{
    /// <summary>The property's name — a second assignment to the same one replaces the first.</summary>
    public string Property { get; } = property;

    public void Apply(UIElement element) => apply(element, resolve());
}

/// <summary>Assigning keys instead of strings: the text follows the language.</summary>
/// <remarks>
/// The key is kept in the element, and forms re-apply it when the language
/// changes — the same way as the theme, with no subscription per element,
/// so nothing can leak. An element that was out of every tree during a change
/// catches up when it is attached.
///
/// A later assignment of a plain string to the same property is not tracked:
/// the key would win again at the next language change. Call Unlocalize
/// to stop following the language.
/// </remarks>
public static class LocalizationExtensions
{
    /// <summary>Localize Text: <c>new Button().Localize(AppText.Save)</c>.</summary>
    public static T Localize<T>(this T element, TextKey key, params object?[] args)
        where T : UIElement, ITextElement =>
        element.Localize(nameof(ITextElement.Text), static (e, text) => e.Text = text, key, args);

    /// <summary>Localize Text with a plural form: <c>label.Localize(AppText.Files, 3)</c>.</summary>
    public static T Localize<T>(this T element, PluralKey key, long count, params object?[] args)
        where T : UIElement, ITextElement =>
        element.Localize(nameof(ITextElement.Text), static (e, text) => e.Text = text, key, count, args);

    /// <summary>Localize any property through a setter:
    /// <c>box.Localize(nameof(TextBox.Watermark), (b, s) => b.Watermark = s, AppText.Search)</c>.</summary>
    /// <param name="property">The property's name. A second call for the same name
    /// replaces the first — the element follows one key per property.</param>
    public static T Localize<T>(
        this T element, string property, Action<T, string> setter, TextKey key, params object?[] args)
        where T : UIElement
    {
        object?[] captured = [.. args];

        element.SetLocalized(new LocalizedBinding(
            property,
            () => Localization.Get(key, captured),
            (e, text) => setter((T)e, text)));

        return element;
    }

    /// <summary>Localize any property with a plural form.</summary>
    public static T Localize<T>(
        this T element, string property, Action<T, string> setter, PluralKey key, long count, params object?[] args)
        where T : UIElement
    {
        object?[] captured = [.. args];

        element.SetLocalized(new LocalizedBinding(
            property,
            () => Localization.Get(key, count, captured),
            (e, text) => setter((T)e, text)));

        return element;
    }

    /// <summary>Stop following the language for a property. The current text stays.</summary>
    public static T Unlocalize<T>(this T element, string property = nameof(ITextElement.Text))
        where T : UIElement
    {
        element.RemoveLocalized(property);
        return element;
    }
}