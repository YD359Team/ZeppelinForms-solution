using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Charts;

public class PieChart : UnitControl
{
    private int _hoveredSlice = -1;

    public List<PieSlice> Slices { get; init; } = [];

    public string? Title { get; set; }
    public Color TitleColor { get; set; } = Colors.Black;
    public Color LabelColor { get; set; } = new Color(255, 90, 90, 90);

    /// <summary>Доля радиуса, вырезаемая в центре. 0 — обычный пирог, 0.5 — кольцо.</summary>
    public float HoleRatio { get; set; }

    public bool ShowLegend { get; set; } = true;
    public bool ShowPercentages { get; set; } = true;
    public float HoverOffset { get; set; } = 6f;

    private float TitleHeight => string.IsNullOrEmpty(Title)
        ? 0
        : TextMeasurer.Current.MeasureText(Title, EffectiveFont).Height + 8f;

    private float LegendWidth
    {
        get
        {
            if (!ShowLegend) return 0;

            float widest = 0;
            foreach (PieSlice slice in Slices)
                widest = Math.Max(widest,
                    TextMeasurer.Current.MeasureText(slice.Label ?? string.Empty, EffectiveFont).Width);

            return widest + 34f;
        }
    }

    private float Total
    {
        get
        {
            float sum = 0;
            foreach (PieSlice slice in Slices)
                sum += Math.Max(0, slice.Value);

            return sum;
        }
    }

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRoundRectangle(LocalBounds, CornerRadius, Background);

        var content = ContentBounds;

        if (!string.IsNullOrEmpty(Title))
            g.DrawText(Title,
                new Rectangle(content.Position, new Size(content.Width, TitleHeight)),
                TitleColor, EffectiveFont,
                HorizontalContentAlignment.Center, VerticalContentAlignment.Center);

        float total = Total;
        if (total <= 0 || Slices.Count == 0) return;

        Rectangle circle = PieBounds;
        float angle = -90f;   // начинаем с 12 часов

        for (int i = 0; i < Slices.Count; i++)
        {
            float value = Math.Max(0, Slices[i].Value);
            float sweep = 360f * value / total;
            Color color = Slices[i].Color ?? ChartPalette.At(i);

            Rectangle sliceBounds = circle;

            if (i == _hoveredSlice)
            {
                // выдвигаем сектор наружу вдоль его биссектрисы
                float mid = (angle + sweep / 2f) * MathF.PI / 180f;
                sliceBounds = new Rectangle(
                    new Point(
                        circle.X + MathF.Cos(mid) * HoverOffset,
                        circle.Y + MathF.Sin(mid) * HoverOffset),
                    circle.Size);
            }

            g.FillPie(sliceBounds, angle, sweep, color);

            if (ShowPercentages && sweep > 18f)
            {
                float mid = (angle + sweep / 2f) * MathF.PI / 180f;
                float radius = circle.Width / 2f * (HoleRatio > 0 ? (1 + HoleRatio) / 2f : 0.62f);

                float cx = sliceBounds.X + sliceBounds.Width / 2f + MathF.Cos(mid) * radius;
                float cy = sliceBounds.Y + sliceBounds.Height / 2f + MathF.Sin(mid) * radius;

                g.DrawText($"{value / total * 100:0}%",
                    new Rectangle(new Point(cx - 20, cy - 8), new Size(40, 16)),
                    Colors.White, EffectiveFont,
                    HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
            }

            angle += sweep;
        }

        if (HoleRatio > 0)
        {
            float hole = circle.Width * HoleRatio;

            g.FillEllipse(
                new Rectangle(
                    new Point(
                        circle.X + (circle.Width - hole) / 2f,
                        circle.Y + (circle.Height - hole) / 2f),
                    new Size(hole, hole)),
                Background.A > 0 ? Background : Colors.White);
        }

        if (ShowLegend)
            DrawLegend(g);
    }

    private Rectangle PieBounds
    {
        get
        {
            var content = ContentBounds;

            float available = Math.Min(
                content.Width - LegendWidth,
                content.Height - TitleHeight);

            float size = Math.Max(0, available - HoverOffset * 2);

            return new Rectangle(
                new Point(
                    content.X + HoverOffset + (content.Width - LegendWidth - size) / 2f,
                    content.Y + TitleHeight + HoverOffset + (content.Height - TitleHeight - size) / 2f),
                new Size(size, size));
        }
    }

    private void DrawLegend(Graphics g)
    {
        var content = ContentBounds;
        float x = content.X + content.Width - LegendWidth + 6f;
        float rowHeight = TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height + 6f;
        float y = content.Y + TitleHeight + (content.Height - TitleHeight - rowHeight * Slices.Count) / 2f;

        for (int i = 0; i < Slices.Count; i++)
        {
            var swatch = new Rectangle(
                new Point(x, y + rowHeight * i + rowHeight / 2f - 5f), new Size(10, 10));

            g.FillRoundRectangle(swatch, new CornerRadius(2f), Slices[i].Color ?? ChartPalette.At(i));

            g.DrawText(Slices[i].Label ?? string.Empty,
                new Rectangle(
                    new Point(x + 16f, y + rowHeight * i),
                    new Size(LegendWidth - 22f, rowHeight)),
                LabelColor, EffectiveFont,
                HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
        }
    }

    protected override void OnMouseExit(MouseMoveEventArgs args)
    {
        Point abs = GetAbsolutePosition();
        Rectangle circle = PieBounds;

        float cx = circle.X + circle.Width / 2f;
        float cy = circle.Y + circle.Height / 2f;

        float dx = args.Location.X - abs.X - cx;
        float dy = args.Location.Y - abs.Y - cy;

        float distance = MathF.Sqrt(dx * dx + dy * dy);
        float radius = circle.Width / 2f;

        int slice = -1;

        if (distance <= radius && distance >= radius * HoleRatio && Total > 0)
        {
            // угол от 12 часов по часовой стрелке, как рисуем секторы
            float angle = MathF.Atan2(dy, dx) * 180f / MathF.PI + 90f;
            if (angle < 0) angle += 360f;

            float accumulated = 0;

            for (int i = 0; i < Slices.Count; i++)
            {
                accumulated += 360f * Math.Max(0, Slices[i].Value) / Total;

                if (angle <= accumulated) { slice = i; break; }
            }
        }

        if (slice == _hoveredSlice) return;

        _hoveredSlice = slice;
        InvalidateVisual();
    }

    protected override void OnMouseLeave() => _hoveredSlice = -1;

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(new Size(260 + LegendWidth + Padding.Horizontal, 200 + Padding.Vertical), availableSize);
}