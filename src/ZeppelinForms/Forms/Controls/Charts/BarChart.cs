using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Charts;

/// <summary>
/// Groups of bars by category, a bar per series.
/// </summary>
/// <remarks>
/// The pointer picks a category: a band marks its group, as the candlestick chart marks
/// a candle, and a readout beside it gives every series' value. The bar right under the
/// pointer is outlined: in a group of several series it says which one is meant.
/// </remarks>
public class BarChart : CartesianChartBase
{
    /// <summary>The category under the pointer; -1 — none.</summary>
    private int _hovered = -1;

    /// <summary>The series of the bar right under the pointer; -1 — none.</summary>
    private int _hoveredSeries = -1;

    /// <summary>The category under the pointer; -1 — none. For tests and for the
    /// accessibility of the chart.</summary>
    internal int HoveredCategory => _hovered;

    /// <summary>The series of the bar under the pointer; -1 — none.</summary>
    internal int HoveredSeries => _hoveredSeries;

    public List<string> Categories { get; init; } = [];
    public List<ChartSeries> Series { get; init; } = [];

    public float BarGap { get; set; } = 0.25f;      // a fraction of the group width
    public CornerRadius BarCornerRadius { get; set; } = new(3f, 3f, 0f, 0f);

    protected override (float Min, float Max) DataRange
    {
        get
        {
            float min = 0, max = 0;

            foreach (ChartSeries series in Series)
                foreach (float value in series.Values)
                {
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }

            return (min, max);
        }
    }

    protected override void DrawContent(Graphics g)
    {
        DrawTitle(g);

        if (Series.Count == 0 || Categories.Count == 0) return;

        DrawValueAxis(g);

        Rectangle plot = PlotArea;
        float groupWidth = plot.Width / Categories.Count;
        float barWidth = groupWidth * (1 - BarGap) / Series.Count;
        float zeroY = ValueToY(0);

        if (_hovered >= 0 && _hovered < Categories.Count)
            g.FillRectangle(
                new Rectangle(new Point(plot.X + groupWidth * _hovered, plot.Y), new Size(groupWidth, plot.Height)),
                HoverColor);

        for (int c = 0; c < Categories.Count; c++)
        {
            float groupX = plot.X + groupWidth * c + groupWidth * BarGap / 2f;

            for (int s = 0; s < Series.Count; s++)
            {
                if (c >= Series[s].Values.Count) continue;

                float value = Series[s].Values[c];
                float y = ValueToY(value);

                // bars can go down from zero for negative values
                var bar = new Rectangle(
                    new Point(groupX + barWidth * s, Math.Min(y, zeroY)),
                    new Size(barWidth - 1f, Math.Abs(zeroY - y)));

                g.FillRoundRectangle(bar, BarCornerRadius, Series[s].Color ?? ChartPalette.At(s));

                if (c == _hovered && s == _hoveredSeries)
                    g.DrawRoundRectangle(bar, BarCornerRadius, TitleColor, 1.5f);
            }

            var labelRect = new Rectangle(
                new Point(plot.X + groupWidth * c, plot.Y + plot.Height + AxisLabelGap),
                new Size(groupWidth, LabelHeight));

            g.DrawText(Categories[c], labelRect, LabelColor, EffectiveFont,
                HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
        }

        if (_hovered >= 0 && _hovered < Categories.Count)
            DrawHoverReadout(g, plot, groupWidth);
    }

    private void DrawHoverReadout(Graphics g, Rectangle plot, float groupWidth)
    {
        var rows = new List<(string, Color)>(Series.Count);

        for (int s = 0; s < Series.Count; s++)
        {
            if (_hovered >= Series[s].Values.Count) continue;

            rows.Add(($"{SeriesName(Series[s], s)}: {FormatValue(Series[s].Values[_hovered])}",
                Series[s].Color ?? ChartPalette.At(s)));
        }

        // beside the group, not over it: the bars stay readable
        float right = plot.X + groupWidth * (_hovered + 1);

        DrawReadout(g, plot, right - 12f, plot.Y + 6f, Categories[_hovered], rows);
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        int category = -1, series = -1;

        if (Series.Count > 0 && Categories.Count > 0)
        {
            Rectangle plot = PlotArea;
            Point pointer = LocalPointer(e.Location);

            if (plot.Width > 0 &&
                pointer.X >= plot.X && pointer.X < plot.X + plot.Width &&
                pointer.Y >= plot.Y && pointer.Y <= plot.Y + plot.Height)
            {
                float groupWidth = plot.Width / Categories.Count;
                category = Math.Clamp((int)((pointer.X - plot.X) / groupWidth), 0, Categories.Count - 1);

                // the bar under the pointer: across the group's bars, and between the
                // zero line and the bar's end
                float barWidth = groupWidth * (1 - BarGap) / Series.Count;
                float offset = pointer.X - (plot.X + groupWidth * category + groupWidth * BarGap / 2f);
                int s = barWidth > 0 ? (int)MathF.Floor(offset / barWidth) : -1;

                if (s >= 0 && s < Series.Count && category < Series[s].Values.Count)
                {
                    float zeroY = ValueToY(0);
                    float y = ValueToY(Series[s].Values[category]);

                    if (pointer.Y >= Math.Min(y, zeroY) && pointer.Y <= Math.Max(y, zeroY))
                        series = s;
                }
            }
        }

        if (category == _hovered && series == _hoveredSeries) return;

        _hovered = category;
        _hoveredSeries = series;
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (_hovered < 0) return;

        _hovered = -1;
        _hoveredSeries = -1;
        InvalidateVisual();
    }
}