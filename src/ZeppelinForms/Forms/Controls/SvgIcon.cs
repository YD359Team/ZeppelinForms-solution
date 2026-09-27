using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// An icon from SVG path data (the d attribute of a single &lt;path&gt;).
/// Full SVG documents with layers/gradients are not supported —
/// they need a separate parser.
/// </summary>
public partial class SvgIcon : DecoratedControl
{
    public string? PathData
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    }

    [Styled(Category = "Appearance")]
    public partial Color Color { get; set; }
    private static Color ColorDefault => Colors.Black;

    /// <summary>0 — fill, greater than zero — a stroke of the given width.</summary>
    public float StrokeWidth
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // switching between fill and stroke changes only the picture
            InvalidateVisual();
        }
    }

    public float IconSize
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the icon size is its desired size under auto-sizing
            Invalidate();
        }
    } = 24f;

    protected override void DrawContent(Graphics g)
    {
        if (string.IsNullOrWhiteSpace(PathData)) return;

        g.DrawSvgPath(PathData, this.ContentBounds, Color, StrokeWidth);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // if given less space than desired, fit into it —
        // DrawSvgPath keeps the proportions anyway
        float size = Math.Min(IconSize, Math.Min(availableSize.Width, availableSize.Height));

        return ResolveSize(new Size(size + Padding.Horizontal, size + Padding.Vertical), availableSize);
    }
}