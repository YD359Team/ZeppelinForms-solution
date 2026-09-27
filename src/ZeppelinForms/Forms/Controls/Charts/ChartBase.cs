using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Charts;

/// <summary>
/// Common to all charts: background, border, title, label color.
/// Axes, grid and the value range live in <see cref="CartesianChartBase"/> —
/// a pie chart doesn't need them.
/// </summary>
public abstract partial class ChartBase : DecoratedControl
{
    public string? Title
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the title takes a strip at the top, and the plot area shrinks by it
            InvalidateVisual();
        }
    }

    /// <summary>The color of axis labels, categories and the legend.
    /// A styled property, so the theme sets it.</summary>
    [Styled(Category = "Chart")]
    public partial Color LabelColor { get; set; }
    private static Color LabelColorDefault => new(255, 90, 90, 90);

    [Styled(Category = "Chart")]
    public partial Color TitleColor { get; set; }
    private static Color TitleColorDefault => Colors.Black;

    protected float TitleHeight => string.IsNullOrEmpty(Title)
        ? 0
        : TextMeasurer.Current.MeasureText(Title, EffectiveFont).Height + 8f;

    protected void DrawTitle(Graphics g)
    {
        if (string.IsNullOrEmpty(Title)) return;

        var content = ContentBounds;

        g.DrawText(Title,
            new Rectangle(content.Position, new Size(content.Width, TitleHeight)),
            TitleColor, EffectiveFont,
            HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(new Size(280 + Padding.Horizontal, 180 + Padding.Vertical), availableSize);
}