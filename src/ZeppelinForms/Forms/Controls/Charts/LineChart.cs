using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Charts;

public class LineChart : ChartBase
{
    public List<string> Categories { get; init; } = [];
    public List<ChartSeries> Series { get; init; } = [];

    /// <summary>Функция для построения графика. Если задана, Series игнорируются.</summary>
    public Func<float, float>? Function { get; set; }

    public float FunctionMinX { get; set; } = -10f;
    public float FunctionMaxX { get; set; } = 10f;
    public int FunctionSamples { get; set; } = 200;

    public float LineWidth { get; set; } = 2f;
    public bool ShowPoints { get; set; } = true;
    public float PointRadius { get; set; } = 3f;

    protected override (float Min, float Max) DataRange
    {
        get
        {
            float min = float.MaxValue, max = float.MinValue;

            if (Function is not null)
            {
                foreach (float y in SampleFunction().Select(p => p.Y))
                {
                    if (!float.IsFinite(y)) continue;   // разрывы вроде 1/x
                    min = Math.Min(min, y);
                    max = Math.Max(max, y);
                }
            }
            else
            {
                foreach (ChartSeries series in Series)
                    foreach (float value in series.Values)
                    {
                        min = Math.Min(min, value);
                        max = Math.Max(max, value);
                    }
            }

            return min > max ? (0f, 1f) : (min, max);
        }
    }

    private IEnumerable<(float X, float Y)> SampleFunction()
    {
        if (Function is null) yield break;

        int samples = Math.Max(2, FunctionSamples);

        for (int i = 0; i < samples; i++)
        {
            float x = FunctionMinX + (FunctionMaxX - FunctionMinX) * i / (samples - 1);

            float y;
            try { y = Function(x); }
            catch { y = float.NaN; }

            yield return (x, y);
        }
    }

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRoundRectangle(LocalBounds, CornerRadius, Background);

        DrawTitle(g);
        DrawValueAxis(g);

        if (Function is not null)
        {
            DrawFunction(g);
            return;
        }

        DrawSeries(g);
    }

    private void DrawFunction(Graphics g)
    {
        Rectangle plot = PlotArea;

        List<Point> segment = [];

        foreach ((float x, float y) in SampleFunction())
        {
            if (!float.IsFinite(y))
            {
                // разрыв: обрываем линию и начинаем новую
                FlushSegment(g, segment);
                continue;
            }

            float px = plot.X + plot.Width * (x - FunctionMinX) / (FunctionMaxX - FunctionMinX);
            float py = ValueToY(y);

            // за пределами области рисовать незачем
            if (py < plot.Y - plot.Height || py > plot.Y + plot.Height * 2)
            {
                FlushSegment(g, segment);
                continue;
            }

            segment.Add(new Point(px, py));
        }

        FlushSegment(g, segment);

        // подписи оси X по краям и в середине
        for (int i = 0; i <= 2; i++)
        {
            float t = i / 2f;
            float x = FunctionMinX + (FunctionMaxX - FunctionMinX) * t;

            var labelRect = new Rectangle(
                new Point(plot.X + plot.Width * t - 30, plot.Y + plot.Height + AxisLabelGap),
                new Size(60, LabelHeight));

            g.DrawText(FormatValue(x), labelRect, LabelColor, EffectiveFont,
                HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
        }
    }

    private void FlushSegment(Graphics g, List<Point> segment)
    {
        if (segment.Count >= 2)
            g.DrawPolyline(segment.ToArray(), Series.FirstOrDefault()?.Color ?? ChartPalette.At(0), LineWidth);

        segment.Clear();
    }

    private void DrawSeries(Graphics g)
    {
        if (Series.Count == 0 || Categories.Count == 0) return;

        Rectangle plot = PlotArea;
        float step = Categories.Count > 1 ? plot.Width / (Categories.Count - 1) : 0;

        for (int s = 0; s < Series.Count; s++)
        {
            ChartSeries series = Series[s];
            Color color = series.Color ?? ChartPalette.At(s);

            Point[] points = new Point[series.Values.Count];

            for (int i = 0; i < series.Values.Count; i++)
                points[i] = new Point(plot.X + step * i, ValueToY(series.Values[i]));

            if (points.Length >= 2)
                g.DrawPolyline(points, color, LineWidth);

            if (!ShowPoints) continue;

            foreach (Point p in points)
                g.FillEllipse(
                    new Rectangle(
                        new Point(p.X - PointRadius, p.Y - PointRadius),
                        new Size(PointRadius * 2, PointRadius * 2)),
                    color);
        }

        for (int c = 0; c < Categories.Count; c++)
        {
            var labelRect = new Rectangle(
                new Point(plot.X + step * c - step / 2f, plot.Y + plot.Height + AxisLabelGap),
                new Size(Math.Max(step, 40), LabelHeight));

            g.DrawText(Categories[c], labelRect, LabelColor, EffectiveFont,
                HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
        }
    }
}