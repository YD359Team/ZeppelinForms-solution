using ZeppelinForms.Drawing;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>The element's font: its own, inherited, or picked from the type ramp.</summary>
public abstract partial class UIElement
{
    /// <summary>A step of the theme's type ramp: the element takes the step's size
    /// and weight, and the family, slant and file — from the font it would have
    /// without the style. <see cref="TextStyle.None"/> — no style.</summary>
    /// <remarks>
    /// <para>
    /// A <see cref="Font"/> set on the element itself is stronger: it is an explicit
    /// word about this very element, while a style is a reference to the theme.
    /// A font the theme set on the element is weaker than the style — otherwise a
    /// theme that gave a type its font would silently switch off every style on it.
    /// </para>
    /// <para>
    /// Not inherited, like a text style in WinUI: a heading style on a panel must
    /// not turn every caption inside it into a heading. For a whole subtree there
    /// is still <see cref="Font"/>, which is inherited.
    /// </para>
    /// </remarks>
    [Styled(Category = "Text", AffectsLayout = true)]
    public partial TextStyle TextStyle { get; set; }

    /// <summary>The font the ramp font was last built from, and the result. EffectiveFont
    /// is read on every measure and every draw, and a new Font per call would be
    /// an allocation per text element per frame. The base is compared by reference:
    /// it is the ancestor's or the form's instance and stays the same between
    /// frames, while a record comparison would walk the family string each time.</summary>
    private Font? _rampBase;
    private Font? _rampFont;

    /// <summary>Own font, or if not set — the nearest one set on an ancestor,
    /// then the form's font, otherwise Font.Default. <see cref="TextStyle"/>,
    /// when set, replaces the size and weight of that font with the step of the
    /// current theme's type ramp — unless the element set a font of its own.</summary>
    public Font EffectiveFont
    {
        get
        {
            Font inherited = GetInheritedValue(FontProperty) ?? FindOwner()?.Font ?? Font.Default;

            TextStyle style = TextStyle;

            if (style == Enums.TextStyle.None || IsLocal(FontProperty) || IsBound(FontProperty))
                return inherited;

            TypeRampStep step = App.Theme.TypeRamp[style];

            // the step is checked against the result rather than remembered apart:
            // a theme switch changes the ramp while the base stays the same instance
            if (ReferenceEquals(_rampBase, inherited) &&
                _rampFont is { } cached &&
                cached.Size == step.Size &&
                cached.Weight == step.Weight)
                return cached;

            _rampBase = inherited;
            _rampFont = inherited with { Size = step.Size, Weight = step.Weight };

            return _rampFont;
        }
    }
}