using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Charts;

/// <summary>
/// Лепестковая диаграмма: несколько рядов по одним и тем же осям.
/// </summary>
/// <remarks>
/// Оси расходятся из центра, по одной на категорию, и значения ряда
/// соединяются в замкнутый контур. Такая диаграмма отвечает на вопрос
/// «где сильнее, где слабее», а не «насколько больше» — сравнивать
/// площади на глаз нельзя, они растут как квадрат значения.
/// Поэтому ряды рисуются полупрозрачной заливкой поверх друг друга,
/// а не сплошной: важна форма, а не размер пятна.
/// </remarks>
public class RadarChart : ChartBase
{
    /// <summary>Подписи осей. Их количество задаёт число лучей.</summary>
    public List<string> Categories { get; init; } = [];

    public List<ChartSeries> Series { get; init; } = [];

    /// <summary>Верх шкалы. null — по наибольшему значению рядов.</summary>
    public float? MaxValue { get; set; }

    /// <summary>Сколько колец рисовать внутри — они же деления шкалы.</summary>
    public int RingCount { get; set; } = 4;

    public bool ShowLegend { get; set; } = true;

    /// <summary>Непрозрачность заливки ряда. Контур всегда сплошной.</summary>
    public float FillOpacity { get; set; } = 0.22f;

    public float PointRadius { get; set; } = 3f;

    public Color GridColor { get; set; } = new(255, 224, 224, 224);
    public Color AxisColor { get; set; } = new(255, 190, 190, 190);

    private float Scale
    {
        get
        {
            if (MaxValue is { } fixedMax && fixedMax > 0) return fixedMax;

            float max = 0;

            foreach (ChartSeries series in Series)
                foreach (float value in series.Values)
                    max = Math.Max(max, value);

            // всё по нулям — шкала всё равно должна быть ненулевой,
            // иначе делить будет не на что
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

    /// <summary>Место под подписи осей: самая длинная подпись уходит вбок
    /// на свою половину, плюс отступ от конца луча.</summary>
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

    /// <summary>Точка на луче категории. Луч 0 смотрит вверх, дальше
    /// по часовой стрелке — так же, как в круговой диаграмме.</summary>
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
        DrawLabels(g, center, radius, axes);

        float scale = Scale;
        Span<Point> vertices = axes <= 32 ? stackalloc Point[axes] : new Point[axes];

        for (int s = 0; s < Series.Count; s++)
        {
            ChartSeries series = Series[s];
            Color color = series.Color ?? ChartPalette.At(s);

            for (int i = 0; i < axes; i++)
            {
                // ряд короче списка осей — недостающее считаем нулём,
                // иначе диаграмма просто не нарисуется
                float value = i < series.Values.Count ? series.Values[i] : 0f;

                vertices[i] = At(center, radius * Math.Clamp(value / scale, 0f, 1f), i, axes);
            }

            g.FillPolygon(vertices, color.WithA((byte)(255 * Math.Clamp(FillOpacity, 0f, 1f))));

            #pragma warning disable CA2014
            // контур замыкаем сами: DrawPolyline линию не закрывает
            Span<Point> outline = axes + 1 <= 33 ? stackalloc Point[axes + 1] : new Point[axes + 1];
            #pragma warning restore CA2014
            vertices.CopyTo(outline);
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

        if (ShowLegend) DrawLegend(g);
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
        float lineHeight = TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height;

        for (int i = 0; i < axes; i++)
        {
            Point anchor = At(center, radius + 10f, i, axes);

            // подпись вешаем на конец луча и выравниваем по той стороне,
            // с которой луч подошёл: иначе текст наедет на диаграмму
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
        float rowHeight = TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height + 6f;
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