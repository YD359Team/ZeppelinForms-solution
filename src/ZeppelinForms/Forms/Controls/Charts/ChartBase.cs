using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Charts;

public abstract class ChartBase : UnitControl
{
    protected const float AxisLabelGap = 4f;

    public string? Title { get; set; }

    public Color AxisColor { get; set; } = new Color(255, 150, 150, 150);
    public Color GridColor { get; set; } = new Color(255, 232, 232, 232);
    public Color LabelColor { get; set; } = new Color(255, 90, 90, 90);
    public Color TitleColor { get; set; } = Colors.Black;

    public bool ShowGrid { get; set; } = true;
    public int GridLineCount { get; set; } = 4;

    /// <summary>Нижняя граница оси значений. null — считать по данным.</summary>
    public float? MinValue { get; set; }
    public float? MaxValue { get; set; }

    protected float TitleHeight => string.IsNullOrEmpty(Title)
        ? 0
        : TextMeasurer.Current.MeasureText(Title, EffectiveFont).Height + 8f;

    protected float LabelHeight => TextMeasurer.Current.MeasureText("0", EffectiveFont).Height;

    protected abstract (float Min, float Max) DataRange { get; }

    protected (float Min, float Max) EffectiveRange
    {
        get
        {
            var (min, max) = DataRange;

            min = MinValue ?? Math.Min(0, min);
            max = MaxValue ?? max;

            // вырожденный диапазон растянем, иначе делить будем на ноль
            if (Math.Abs(max - min) < 0.0001f)
                max = min + 1f;

            return (min, max);
        }
    }

    /// <summary>Ширина полосы под подписи оси значений — по самой длинной из них.</summary>
    protected float ValueAxisWidth
    {
        get
        {
            var (min, max) = EffectiveRange;

            float widest = 0;
            for (int i = 0; i <= GridLineCount; i++)
            {
                float value = min + (max - min) * i / GridLineCount;
                widest = Math.Max(widest, TextMeasurer.Current.MeasureText(FormatValue(value), EffectiveFont).Width);
            }

            return widest + AxisLabelGap * 2;
        }
    }

    protected virtual string FormatValue(float value) => value.ToString("0.##");

    protected Rectangle PlotArea
    {
        get
        {
            var content = ContentBounds;
            float left = ValueAxisWidth;
            float bottom = LabelHeight + AxisLabelGap * 2;
            float top = TitleHeight;

            return new Rectangle(
                new Point(content.X + left, content.Y + top),
                new Size(
                    Math.Max(0, content.Width - left),
                    Math.Max(0, content.Height - top - bottom)));
        }
    }

    protected void DrawTitle(Graphics g)
    {
        if (string.IsNullOrEmpty(Title)) return;

        var content = ContentBounds;

        g.DrawText(Title,
            new Rectangle(content.AsPosition(), new Size(content.Width, TitleHeight)),
            TitleColor, EffectiveFont,
            HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
    }

    protected void DrawValueAxis(Graphics g)
    {
        Rectangle plot = PlotArea;
        var (min, max) = EffectiveRange;

        for (int i = 0; i <= GridLineCount; i++)
        {
            float t = i / (float)GridLineCount;
            float y = plot.Y + plot.Height * (1 - t);
            float value = min + (max - min) * t;

            if (ShowGrid && i > 0)
                g.DrawLine(new Point(plot.X, y), new Point(plot.X + plot.Width, y), GridColor, 1f);

            var labelRect = new Rectangle(
                new Point(ContentBounds.X, y - LabelHeight / 2f),
                new Size(plot.X - ContentBounds.X - AxisLabelGap, LabelHeight));

            g.DrawText(FormatValue(value), labelRect, LabelColor, EffectiveFont,
                HorizontalContentAlignment.Right, VerticalContentAlignment.Center);
        }

        g.DrawLine(new Point(plot.X, plot.Y), new Point(plot.X, plot.Y + plot.Height), AxisColor, 1.2f);
        g.DrawLine(new Point(plot.X, plot.Y + plot.Height),
            new Point(plot.X + plot.Width, plot.Y + plot.Height), AxisColor, 1.2f);
    }

    protected float ValueToY(float value)
    {
        Rectangle plot = PlotArea;
        var (min, max) = EffectiveRange;

        float t = (value - min) / (max - min);
        return plot.Y + plot.Height * (1 - t);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(new Size(280 + Padding.Horizontal, 180 + Padding.Vertical), availableSize);
}
