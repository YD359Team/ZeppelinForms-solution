using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Charts;

/// <summary>
/// A radar chart: several series along the same axes.
/// </summary>
/// <remarks>
/// The axes spread from the center, one per category, and the values of a series
/// are joined into a closed outline. Such a chart answers the question "where is it
/// stronger, where weaker" rather than "how much more" — areas can't be compared
/// by eye, they grow as the square of the value. So the series are drawn with
/// a translucent fill on top of each other rather than a solid one: the shape
/// matters, not the size of the patch.
/// <para>
/// The pointer picks the ray nearest to it by angle, anywhere over the chart's disc: the
/// ray is drawn in full, every series' point on it grows, and a readout gives their
/// values. Over a ray the eye compares series exactly where the shapes can't tell.
/// </para>
/// </remarks>
public partial class RadarChart : ChartBase
{
    /// <summary>The axis under the pointer; -1 — none.</summary>
    private int _hovered = -1;

    /// <summary>The axis under the pointer, of <see cref="Categories"/>; -1 — none.
    /// For tests and for the accessibility of the chart.</summary>
    internal int HoveredAxis => _hovered;

    /// <summary>Axis labels. Their count sets the number of rays.</summary>
    public List<string> Categories { get; init; } = [];

    public List<ChartSeries> Series { get; init; } = [];

    /// <summary>The top of the scale. null — by the largest value of the series.</summary>
    public float? MaxValue { get; set; }

    /// <summary>How many rings to draw inside — they are also the scale divisions.</summary>
    public int RingCount { get; set; } = 4;

    public bool ShowLegend { get; set; } = true;

    /// <summary>The opacity of a series fill. The outline is always solid.</summary>
    public float FillOpacity { get; set; } = 0.22f;

    public float PointRadius { get; set; } = 3f;

    [Styled(Category = "Chart")]
    public partial Color GridColor { get; set; }
    private static Color GridColorDefault => new(255, 224, 224, 224);

    [Styled(Category = "Chart")]
    public partial Color AxisColor { get; set; }
    private static Color AxisColorDefault => new(255, 190, 190, 190);

    private float Scale
    {
        get
        {
            if (MaxValue is { } fixedMax && fixedMax > 0) return fixedMax;

            float max = 0;

            foreach (ChartSeries series in Series)
                foreach (float value in series.Values)
                    max = Math.Max(max, value);

            // all zeros — the scale must still be non-zero,
            // otherwise there would be nothing to divide by
            return max > 0 ? max : 1f;
        }
    }

    private float LegendWidth
    {
        get
        {
            if (!ShowLegend || Series.Count == 0) return 0;

            float widest = 0;

            foreach (ChartSeries series in Series)
                widest = Math.Max(widest,
                    TextMeasurer.Current.MeasureText(series.Name ?? string.Empty, EffectiveFont).Width);

            return widest + 34f;
        }
    }

    /// <summary>Room for the axis labels: the longest label extends sideways
    /// by its half, plus a gap from the end of the ray.</summary>
    private float LabelMargin
    {
        get
        {
            float widest = 0;

            foreach (string category in Categories)
                widest = Math.Max(widest,
                    TextMeasurer.Current.MeasureText(category, EffectiveFont).Width);

            return widest / 2f + 12f;
        }
    }

    private (Point Center, float Radius) Layout
    {
        get
        {
            Rectangle content = ContentBounds;

            float width = content.Width - LegendWidth;
            float height = content.Height - TitleHeight;

            float margin = LabelMargin;
            float radius = Math.Max(0, Math.Min(width, height) / 2f - margin);

            return (
                new Point(content.X + width / 2f, content.Y + TitleHeight + height / 2f),
                radius);
        }
    }

    /// <summary>A point on a category's ray. Ray 0 points up, then clockwise —
    /// the same as in the pie chart.</summary>
    private static Point At(Point center, float radius, int index, int count)
    {
        float angle = (-90f + 360f * index / count) * MathF.PI / 180f;

        return new Point(
            center.X + MathF.Cos(angle) * radius,
            center.Y + MathF.Sin(angle) * radius);
    }

    protected override void DrawContent(Graphics g)
    {
        DrawTitle(g);

        int axes = Categories.Count;
        if (axes < 3) return;

        var (center, radius) = Layout;
        if (radius <= 0) return;

        DrawGrid(g, center, radius, axes);

        // the hovered ray over the grid, under the series: in the labels' color, so it
        // reads as the one the readout speaks of
        if (_hovered >= 0 && _hovered < axes)
            g.DrawLine(center, At(center, radius, _hovered, axes), LabelColor, 1.5f);

        DrawLabels(g, center, radius, axes);

        float scale = Scale;

        // one buffer for every series, taken outside the loop (a stackalloc inside it
        // would grow the stack with each series, CA2014): the polygon is its first
        // points, the outline the same points with the first repeated at the end —
        // DrawPolyline doesn't close the line
        Span<Point> outline = axes + 1 <= 33 ? stackalloc Point[axes + 1] : new Point[axes + 1];
        Span<Point> vertices = outline[..axes];

        for (int s = 0; s < Series.Count; s++)
        {
            ChartSeries series = Series[s];
            Color color = series.Color ?? ChartPalette.At(s);

            for (int i = 0; i < axes; i++)
            {
                // a series shorter than the list of axes — the missing values count
                // as zero, otherwise the chart simply wouldn't be drawn
                float value = i < series.Values.Count ? series.Values[i] : 0f;

                vertices[i] = At(center, radius * Math.Clamp(value / scale, 0f, 1f), i, axes);
            }

            g.FillPolygon(vertices, color.WithA((byte)(255 * Math.Clamp(FillOpacity, 0f, 1f))));

            // the outline is closed by us: DrawPolyline doesn't close the line
            outline[axes] = vertices[0];

            g.DrawPolyline(outline, color, 2f);

            if (PointRadius > 0)
                foreach (Point vertex in vertices)
                    g.FillEllipse(
                        new Rectangle(
                            new Point(vertex.X - PointRadius, vertex.Y - PointRadius),
                            new Size(PointRadius * 2, PointRadius * 2)),
                        color);
        }

        if (_hovered >= 0 && _hovered < axes)
            DrawHover(g, center, radius, axes, scale);

        if (ShowLegend) DrawLegend(g);
    }


    /// <summary>Every series' point on the hovered ray, grown, and the readout of their values.</summary>
    private void DrawHover(Graphics g, Point center, float radius, int axes, float scale)
    {
        var rows = new List<(string, Color)>(Series.Count);

        for (int s = 0; s < Series.Count; s++)
        {
            ChartSeries series = Series[s];
            Color color = series.Color ?? ChartPalette.At(s);

            // a missing value counts as zero, as it is drawn
            float value = _hovered < series.Values.Count ? series.Values[_hovered] : 0f;

            DrawHoveredPoint(g, At(center, radius * Math.Clamp(value / scale, 0f, 1f), _hovered, axes), PointRadius, color);
            rows.Add(($"{SeriesName(series, s)}: {value.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)}", color));
        }

        // beside the end of the ray, inside the chart's disc and its labels
        Point end = At(center, radius, _hovered, axes);
        Rectangle content = ContentBounds;
        var area = new Rectangle(content.Position, new Size(content.Width - LegendWidth, content.Height));

        DrawReadout(g, area, end.X, end.Y, Categories[_hovered], rows);
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        int axis = -1;
        int axes = Categories.Count;

        if (axes >= 3)
        {
            var (center, radius) = Layout;
            Point pointer = LocalPointer(e.Location);

            float dx = pointer.X - center.X;
            float dy = pointer.Y - center.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);

            // over the disc and its labels, not right at the center, where every ray
            // is equally near and the choice would jump with each pixel
            if (radius > 0 && distance <= radius + LabelMargin && distance >= Math.Min(8f, radius / 4f))
            {
                // the angle from 12 o'clock clockwise, as the rays go
                float angle = MathF.Atan2(dy, dx) * 180f / MathF.PI + 90f;
                if (angle < 0) angle += 360f;

                axis = (int)MathF.Round(angle / (360f / axes)) % axes;
            }
        }

        if (axis == _hovered) return;

        _hovered = axis;
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (_hovered < 0) return;

        _hovered = -1;
        InvalidateVisual();
    }

    private void DrawGrid(Graphics g, Point center, float radius, int axes)
    {
        Span<Point> ring = axes + 1 <= 33 ? stackalloc Point[axes + 1] : new Point[axes + 1];

        for (int r = 1; r <= RingCount; r++)
        {
            float ringRadius = radius * r / RingCount;

            for (int i = 0; i < axes; i++)
                ring[i] = At(center, ringRadius, i, axes);

            ring[axes] = ring[0];

            g.DrawPolyline(ring, GridColor, 1f);
        }

        for (int i = 0; i < axes; i++)
            g.DrawLine(center, At(center, radius, i, axes), AxisColor, 1f);
    }

    private void DrawLabels(Graphics g, Point center, float radius, int axes)
    {
        float lineHeight = TextMeasurer.LineHeight(EffectiveFont);

        for (int i = 0; i < axes; i++)
        {
            Point anchor = At(center, radius + 10f, i, axes);

            // the label is hung on the end of the ray and aligned by the side
            // the ray came from: otherwise the text would run over the chart
            float width = LabelMargin * 2f;

            var rect = new Rectangle(
                new Point(anchor.X - width / 2f, anchor.Y - lineHeight / 2f),
                new Size(width, lineHeight));

            g.DrawText(Categories[i], rect, LabelColor, EffectiveFont,
                HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
        }
    }

    private void DrawLegend(Graphics g)
    {
        Rectangle content = ContentBounds;

        float x = content.X + content.Width - LegendWidth + 6f;
        float rowHeight = TextMeasurer.LineHeight(EffectiveFont) + 6f;
        float y = content.Y + TitleHeight + (content.Height - TitleHeight - rowHeight * Series.Count) / 2f;

        for (int i = 0; i < Series.Count; i++)
        {
            var swatch = new Rectangle(
                new Point(x, y + rowHeight * i + rowHeight / 2f - 5f), new Size(10, 10));

            g.FillRoundRectangle(swatch, new CornerRadius(2f), Series[i].Color ?? ChartPalette.At(i));

            g.DrawText(Series[i].Name ?? string.Empty,
                new Rectangle(new Point(x + 16f, y + rowHeight * i), new Size(LegendWidth - 22f, rowHeight)),
                LabelColor, EffectiveFont,
                HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(
            new Size(240 + LegendWidth + Padding.Horizontal, 220 + Padding.Vertical),
            availableSize);
}