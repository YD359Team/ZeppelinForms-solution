using ZeppelinForms.Accessibility;
using ZeppelinForms.Animation;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// The surface a tooltip is shown on: the theme's surface color, a hairline edge, a soft
/// shadow and the rounding the theme gives to what floats over the content. The form
/// makes one for the element whose <see cref="UIElement.ToolTip"/> or
/// <see cref="UIElement.ToolTipContent"/> is to be shown.
/// </summary>
/// <remarks>
/// <para>
/// A control of its own, so that a style sheet reaches it as any other:
/// <c>ToolTip { Background: @Accent; CornerRadius: 2; }</c>, and <c>ToolTip Label</c> for
/// its text. It used to be a plain border with a black edge and a fixed pale fill, made
/// on the spot — no theme and no sheet could touch it, and in a dark theme it stayed light.
/// </para>
/// <para>
/// A text longer than <see cref="MaxContentWidth"/> wraps instead of running across the
/// window, as tooltips do in WinUI. The tooltip fades in (<see cref="UIElement.EnterTransition"/>),
/// unless motion is reduced.
/// </para>
/// </remarks>
public partial class ToolTip : DecoratedWrapControl
{
    public ToolTip()
    {
        // it never takes the pointer: under it is what the user was pointing at
        IsHitTestVisible = false;
        AccessibleRole = AccessibilityRole.ToolTip;

        EnterTransition = VisibilityTransition.Fade(150);

        // a control default, so the theme and the style sheets come before it
        SetControlDefault(PaddingProperty, new Thickness(8, 5, 8, 6));
    }

    /// <summary>A tooltip of a text.</summary>
    public ToolTip(string? text) : this()
    {
        Text = text;
    }

    /// <summary>A tooltip of any content: an image, a few lines of different weight.</summary>
    public ToolTip(UIElement content) : this()
    {
        Child = content;
    }

    /// <summary>The text shown: a label of its own, captioned and wrapped at
    /// <see cref="MaxContentWidth"/>. Null when the content is something else.</summary>
    public string? Text
    {
        get => Child is TextPart part ? part.Text : null;
        set
        {
            if (Child is TextPart part)
                part.Text = value;
            else
                Child = new TextPart { Text = value };
        }
    }

    /// <summary>The widest the content may be; a longer text wraps. 320 as in WinUI.</summary>
    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial float MaxContentWidth { get; set; }
    private static float MaxContentWidthDefault => 320f;

    protected override Size MeasureOverride(Size availableSize)
    {
        // the form measures its overlays in unlimited room: the cap is the tooltip's own
        float width = Math.Min(availableSize.Width, MaxContentWidth + Padding.Horizontal);

        return base.MeasureOverride(new Size(width, availableSize.Height));
    }

    /// <summary>The text of a tooltip: the type ramp's caption, wrapped. A label of its
    /// own so that both are control defaults — <c>ToolTip Label { TextStyle: Body; }</c>
    /// in a sheet still wins over them.</summary>
    private sealed class TextPart : Label
    {
        public TextPart()
        {
            IsHitTestVisible = false;

            SetControlDefault(TextStyleProperty, Enums.TextStyle.Caption);
            SetControlDefault(TextWrappingProperty, TextWrapping.Wrap);
        }
    }
}
