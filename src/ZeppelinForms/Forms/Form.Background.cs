using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms;

/// <summary>The color of the window itself, under its content.</summary>
public partial class Form
{
    /// <summary>The color the window is cleared with before its content is drawn.
    /// Null — the theme's Background, following a theme change.</summary>
    /// <remarks>Until 0.16 the window was cleared to a fixed white whatever the theme:
    /// panels draw no background of their own, and in a dark theme every gap between
    /// the cards of a page showed white.</remarks>
    public Color? Background
    {
        get;
        set
        {
            if (Nullable.Equals(field, value)) return;

            field = value;
            InvalidateVisual();
        }
    }

    /// <summary>What the window is cleared with: its own color, or the theme's.</summary>
    internal Color EffectiveBackground => Background ?? App.Theme.Colors.Background;
}