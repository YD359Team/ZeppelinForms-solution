using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Lays out children in a row and wraps to the next line when the current
/// one runs out. Along an unbounded axis there is no wrapping: there is
/// nothing to wrap if space is infinite.
/// </summary>
public partial class WrapPanel : DecoratedPanel
{
    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial Orientation Orientation { get; set; }

    /// <summary>The gap between neighbours in one line.</summary>
    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial float Spacing { get; set; }

    /// <summary>The gap between lines.</summary>
    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial float LineSpacing { get; set; }

    /// <summary>Alignment of a line along its cross axis: short elements are pressed
    /// to the start of the line, to the center, or stretched to its height.</summary>
    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial CrossAxisAlignment LineAlignment { get; set; }

    private static CrossAxisAlignment LineAlignmentDefault => CrossAxisAlignment.Start;

    private bool IsHorizontal => Orientation == Orientation.Horizontal;

    private float MainOf(Size size) => IsHorizontal ? size.Width : size.Height;
    private float CrossOf(Size size) => IsHorizontal ? size.Height : size.Width;
    private float MainMargin(Thickness m) => IsHorizontal ? m.Horizontal : m.Vertical;
    private float CrossMargin(Thickness m) => IsHorizontal ? m.Vertical : m.Horizontal;

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        float limit = IsHorizontal ? inner.Width : inner.Height;

        // along an infinite axis there is nothing to wrap: everything fits into one line
        bool wraps = float.IsFinite(limit);

        float lineMain = 0;
        float lineCross = 0;
        float totalMain = 0;
        float totalCross = 0;
        bool firstInLine = true;

        foreach (UIElement child in Children)
        {
            if (!child.IsVisible) continue;

            Thickness m = child.Margin;

            // measure in a full line: an element wider than it takes the whole
            // line anyway, and it must not be cut at the measuring stage
            child.Measure(IsHorizontal
                ? new Size(Math.Max(0, limit - m.Horizontal), float.PositiveInfinity)
                : new Size(float.PositiveInfinity, Math.Max(0, limit - m.Vertical)));

            float main = MainOf(child.DesiredSize) + MainMargin(m);
            float cross = CrossOf(child.DesiredSize) + CrossMargin(m);

            float advance = firstInLine ? main : main + Spacing;

            if (wraps && !firstInLine && lineMain + advance > limit)
            {
                totalMain = Math.Max(totalMain, lineMain);
                totalCross += lineCross + LineSpacing;

                lineMain = main;
                lineCross = cross;
                firstInLine = false;
                continue;
            }

            lineMain += advance;
            lineCross = Math.Max(lineCross, cross);
            firstInLine = false;
        }

        totalMain = Math.Max(totalMain, lineMain);
        totalCross += lineCross;

        return IsHorizontal
            ? new Size(totalMain + Padding.Horizontal, totalCross + Padding.Vertical)
            : new Size(totalCross + Padding.Horizontal, totalMain + Padding.Vertical);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        var inner = new Size(
            Math.Max(0, contentSize.Width - Padding.Horizontal),
            Math.Max(0, contentSize.Height - Padding.Vertical));

        float limit = IsHorizontal ? inner.Width : inner.Height;

        float mainOffset = 0;
        float crossOffset = 0;
        float lineCross = 0;
        bool firstInLine = true;

        // the line is arranged right away, but its cross size is known
        // only after the line has ended — so we accumulate
        List<UIElement> line = [];

        foreach (UIElement child in Children)
        {
            if (!child.IsVisible) continue;

            Thickness m = child.Margin;

            float main = MainOf(child.DesiredSize) + MainMargin(m);
            float cross = CrossOf(child.DesiredSize) + CrossMargin(m);
            float advance = firstInLine ? main : main + Spacing;

            if (!firstInLine && mainOffset + advance > limit)
            {
                PlaceLine(line, crossOffset, lineCross);

                crossOffset += lineCross + LineSpacing;
                mainOffset = 0;
                lineCross = 0;
                line.Clear();
                advance = main;
            }

            child.Arrange(ToPoint(mainOffset + (IsHorizontal ? m.Left : m.Top),
                          crossOffset + (IsHorizontal ? m.Top : m.Left)),
                          child.DesiredSize);

            line.Add(child);

            mainOffset += advance;
            lineCross = Math.Max(lineCross, cross);
            firstInLine = false;
        }

        PlaceLine(line, crossOffset, lineCross);
    }

    /// <summary>Build a point from the main and cross coordinates.
    /// Not named Position: that is a UIElement property, and the name
    /// would hide it inside this class.</summary>
    private Point ToPoint(float main, float cross) => IsHorizontal
        ? new Point(Padding.Left + main, Padding.Top + cross)
        : new Point(Padding.Left + cross, Padding.Top + main);

    /// <summary>Shift the line's elements along its cross axis
    /// once its height is known.</summary>
    private void PlaceLine(List<UIElement> line, float crossOffset, float lineCross)
    {
        if (LineAlignment == CrossAxisAlignment.Start || line.Count == 0) return;

        foreach (UIElement child in line)
        {
            Thickness m = child.Margin;
            float cross = CrossOf(child.ActualSize) + CrossMargin(m);

            if (LineAlignment == CrossAxisAlignment.Stretch)
            {
                child.Arrange(child.Position, IsHorizontal
                    ? new Size(child.ActualSize.Width, Math.Max(0, lineCross - CrossMargin(m)))
                    : new Size(Math.Max(0, lineCross - CrossMargin(m)), child.ActualSize.Height));

                continue;
            }

            float shift = LineAlignment == CrossAxisAlignment.Center
                ? (lineCross - cross) / 2f
                : lineCross - cross;

            child.Position = IsHorizontal
                ? new Point(child.Position.X, child.Position.Y + shift)
                : new Point(child.Position.X + shift, child.Position.Y);
        }
    }
}