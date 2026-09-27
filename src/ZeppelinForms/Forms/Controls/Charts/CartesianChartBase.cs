using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Charts;

/// <summary>
/// A chart with a value axis and a grid: bars, lines and everything
/// that is laid out on a coordinate plane.
/// </summary>
public abstract partial class CartesianChartBase : ChartBase
{
    protected const float AxisLabelGap = 4f;

    [Styled(Category = "Chart")]
    public partial Color AxisColor { get; set; }
    private static Color AxisColorDefault => new(255, 150, 150, 150);

    [Styled(Category = "Chart")]
    public partial Color GridColor { get; set; }
    private static Color GridColorDefault => new(255, 232, 232, 232);

    public bool ShowGrid { get; set; } = true;
    public int GridLineCount { get; set; } = 4;

    /// <summary>The lower bound of the value axis. null — compute from the data.</summary>
    public float? MinValue { get; set; }
    public float? MaxValue { get; set; }

    /// <summary>The number of grid divisions, never below one: with zero divisions
    /// the axis was divided by zero, and its labels and lines went to NaN.</summary>
    private int Divisions => Math.Max(1, GridLineCount);

    protected float LabelHeight => TextMeasurer.Current.MeasureText("0", EffectiveFont).Height;

    protected abstract (float Min, float Max) DataRange { get; }

    /// <summary>Whether to extend the value axis down to zero. Bars can't do without
    /// zero: their length is the value. Prices are the opposite: a zero price is
    /// never needed, and with it all the candles collapse into a strip at the top.</summary>
    protected virtual bool IncludeZero => true;

    protected (float Min, float Max) EffectiveRange
    {
        get
        {
            var (min, max) = DataRange;

            min = MinValue ?? (IncludeZero ? Math.Min(0, min) : min);
            max = MaxValue ?? max;

            // a degenerate range is stretched, otherwise we would divide by zero
            if (Math.Abs(max - min) < 0.0001f)
                max = min + 1f;

            return (min, max);
        }
    }

    /// <summary>The width of the strip for the value axis labels — by the longest of them.</summary>
    protected float ValueAxisWidth
    {
        get
        {
            var (min, max) = EffectiveRange;
            int divisions = Divisions;

            float widest = 0;
            for (int i = 0; i <= divisions; i++)
            {
                float value = min + (max - min) * i / divisions;
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

    protected void DrawValueAxis(Graphics g)
    {
        Rectangle plot = PlotArea;
        var (min, max) = EffectiveRange;
        int divisions = Divisions;

        for (int i = 0; i <= divisions; i++)
        {
            float t = i / (float)divisions;
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
}