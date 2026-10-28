using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms;

/// <summary>The language of the form: its title as a key, and its tree re-texted when the
/// language changes.</summary>
public partial class Form
{
    /// <summary>The window title as a key: follows the language like the controls' texts.</summary>
    public Form LocalizeTitle(TextKey key, params object?[] args)
    {
        object?[] captured = [.. args];

        _localizedTitle = () => Localization.Get(key, captured);
        Title = _localizedTitle();

        return this;
    }

    private void OnLocalizationChanged(object? sender, EventArgs e)
    {
        if (_localizedTitle is not null)
            Title = _localizedTitle();

        if (Content is not null)
            Walk(Content, static element => element.ApplyLocalization());

        // overlays live separately from Content: an open menu or flyout
        // must not stay in the old language until it is closed
        foreach (UIElement overlay in _overlays.ToArray())
            Walk(overlay, static element => element.ApplyLocalization());

        // a right-to-left language changes what :rtl and :ltr select
        RestyleForDirection();

        // texts of another language have other lengths, and a right-to-left
        // language mirrors the whole layout — every measure is stale
        InvalidateMeasureTree();

        Invalidate();
    }

    /// <summary>The window title as a key, if it was given as one.</summary>
    private Func<string>? _localizedTitle;
}
