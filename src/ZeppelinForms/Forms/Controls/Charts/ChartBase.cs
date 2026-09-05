using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Charts;

/// <summary>
/// Общее для всех диаграмм: фон, рамка, заголовок, цвет подписей.
/// Оси, сетка и диапазон значений вынесены в <see cref="CartesianChartBase"/> —
/// круговой диаграмме они не нужны.
/// </summary>
public abstract class ChartBase : DecoratedControl
{
    public string? Title { get; set; }

    public Color LabelColor { get; set; } = new Color(255, 90, 90, 90);
    public Color TitleColor { get; set; } = Colors.Black;

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
