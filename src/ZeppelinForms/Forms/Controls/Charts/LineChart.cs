using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Charts;

/// <summary>
/// Lines of values by category, or the graph of a function.
/// </summary>
/// <remarks>
/// The pointer picks the nearest category: a band marks it, its points grow, and a
/// readout beside it gives every series' value there. Over a function the pointer's
/// own argument is read: a guide line, the point of the curve under it and its value.
/// </remarks>
public class LineChart : CartesianChartBase
{
    /// <summary>The category under the pointer; -1 — none.</summary>
    private int _hovered = -1;

    /// <summary>The function's argument under the pointer; NaN — none.</summary>
    private float _hoveredX = float.NaN;

    /// <summary>The category under the pointer, of the series' categories; -1 — none.
    /// For tests and for the accessibility of the chart.</summary>
    internal int HoveredCategory => _hovered;

    /// <summary>The function's argument under the pointer; NaN — none.</summary>
    internal float HoveredArgument => _hoveredX;

    public List<string> Categories { get; init; } = [];
    public List<ChartSeries> Series { get; init; } = [];

    /// <summary>A function to plot. If set, Series are ignored.</summary>
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
                    if (!float.IsFinite(y)) continue;   // discontinuities like 1/x
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

    protected override void DrawContent(Graphics g)
    {
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
        // an empty or inverted argument range has nothing to plot, and the
        // mapping to the plot's width below would divide by zero
        if (FunctionMaxX <= FunctionMinX) return;

        Rectangle plot = PlotArea;

        List<Point> segment = [];

        foreach ((float x, float y) in SampleFunction())
        {
            if (!float.IsFinite(y))
            {
                // a discontinuity: break the line and start a new one
                FlushSegment(g, segment);
                continue;
            }

            float px = plot.X + plot.Width * (x - FunctionMinX) / (FunctionMaxX - FunctionMinX);
            float py = ValueToY(y);

            // there is no point drawing beyond the area
            if (py < plot.Y - plot.Height || py > plot.Y + plot.Height * 2)
            {
                FlushSegment(g, segment);
                continue;
            }

            segment.Add(new Point(px, py));
        }

        FlushSegment(g, segment);

        DrawFunctionHover(g, plot);

        // X axis labels at the edges and in the middle
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

    /// <summary>The guide at the pointer's argument, the curve's point there and its value.</summary>
    private void DrawFunctionHover(Graphics g, Rectangle plot)
    {
        if (float.IsNaN(_hoveredX) || Function is null) return;

        float px = plot.X + plot.Width * (_hoveredX - FunctionMinX) / (FunctionMaxX - FunctionMinX);

        g.DrawLine(new Point(px, plot.Y), new Point(px, plot.Y + plot.Height), AxisColor, 1f);

        float y = Evaluate(_hoveredX);
        Color color = Series.FirstOrDefault()?.Color ?? ChartPalette.At(0);

        string value = float.IsFinite(y) ? FormatValue(y) : "—";

        if (float.IsFinite(y))
        {
            float py = ValueToY(y);

            if (py >= plot.Y && py <= plot.Y + plot.Height)
                DrawHoveredPoint(g, new Point(px, py), PointRadius, color);
        }

        DrawReadout(g, plot, px, plot.Y + 6f, $"x = {FormatValue(_hoveredX)}", [($"y = {value}", color)]);
    }

    /// <summary>The function's value; NaN where it throws, as the sampling takes it.</summary>
    private float Evaluate(float x)
    {
        try { return Function!(x); }
        catch { return float.NaN; }
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

        // the band of the hovered category, under the lines: half a step to each side,
        // cut by the plot at the first and the last category
        if (_hovered >= 0 && _hovered < Categories.Count)
        {
            float center = plot.X + step * _hovered;
            float half = step > 0 ? step / 2f : plot.Width / 2f;
            float left = Math.Max(plot.X, center - half);
            float right = Math.Min(plot.X + plot.Width, center + half);

            g.FillRectangle(new Rectangle(new Point(left, plot.Y), new Size(right - left, plot.Height)), HoverColor);
        }

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

        if (_hovered >= 0 && _hovered < Categories.Count)
            DrawSeriesHover(g, plot, step);
    }

    /// <summary>The hovered category's points, grown, and the readout of its values.</summary>
    private void DrawSeriesHover(Graphics g, Rectangle plot, float step)
    {
        float x = plot.X + step * _hovered;
        var rows = new List<(string, Color)>(Series.Count);

        for (int s = 0; s < Series.Count; s++)
        {
            ChartSeries series = Series[s];
            if (_hovered >= series.Values.Count) continue;

            Color color = series.Color ?? ChartPalette.At(s);
            float value = series.Values[_hovered];

            DrawHoveredPoint(g, new Point(x, ValueToY(value)), PointRadius, color);
            rows.Add(($"{SeriesName(series, s)}: {FormatValue(value)}", color));
        }

        DrawReadout(g, plot, x, plot.Y + 6f, Categories[_hovered], rows);
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        Rectangle plot = PlotArea;
        Point pointer = LocalPointer(e.Location);

        bool inside = pointer.X >= plot.X && pointer.X <= plot.X + plot.Width &&
                      pointer.Y >= plot.Y && pointer.Y <= plot.Y + plot.Height;

        if (Function is not null)
        {
            float argument = inside && FunctionMaxX > FunctionMinX && plot.Width > 0
                ? FunctionMinX + (FunctionMaxX - FunctionMinX) * (pointer.X - plot.X) / plot.Width
                : float.NaN;

            // NaN != NaN: two misses in a row are no change
            if (argument.Equals(_hoveredX)) return;

            _hoveredX = argument;
            InvalidateVisual();
            return;
        }

        int index = -1;

        if (inside && Categories.Count > 0)
        {
            index = Categories.Count == 1
                ? 0
                : Math.Clamp((int)MathF.Round((pointer.X - plot.X) / (plot.Width / (Categories.Count - 1))), 0, Categories.Count - 1);
        }

        if (index == _hovered) return;

        _hovered = index;
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (_hovered < 0 && float.IsNaN(_hoveredX)) return;

        _hovered = -1;
        _hoveredX = float.NaN;
        InvalidateVisual();
    }
}