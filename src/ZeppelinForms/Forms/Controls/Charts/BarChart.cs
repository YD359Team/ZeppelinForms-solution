using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Charts;

public class BarChart : CartesianChartBase
{
    public List<string> Categories { get; init; } = [];
    public List<ChartSeries> Series { get; init; } = [];

    public float BarGap { get; set; } = 0.25f;      // доля от ширины группы
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

        for (int c = 0; c < Categories.Count; c++)
        {
            float groupX = plot.X + groupWidth * c + groupWidth * BarGap / 2f;

            for (int s = 0; s < Series.Count; s++)
            {
                if (c >= Series[s].Values.Count) continue;

                float value = Series[s].Values[c];
                float y = ValueToY(value);

                // столбики умеют уходить вниз от нуля при отрицательных значениях
                var bar = new Rectangle(
                    new Point(groupX + barWidth * s, Math.Min(y, zeroY)),
                    new Size(barWidth - 1f, Math.Abs(zeroY - y)));

                g.FillRoundRectangle(bar, BarCornerRadius, Series[s].Color ?? ChartPalette.At(s));
            }

            var labelRect = new Rectangle(
                new Point(plot.X + groupWidth * c, plot.Y + plot.Height + AxisLabelGap),
                new Size(groupWidth, LabelHeight));

            g.DrawText(Categories[c], labelRect, LabelColor, EffectiveFont,
                HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
        }
    }
}