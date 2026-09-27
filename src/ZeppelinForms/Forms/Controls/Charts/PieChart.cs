using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Charts;

public class PieChart : ChartBase
{
    private int _hoveredSlice = -1;

    public List<PieSlice> Slices { get; init; } = [];

    /// <summary>The fraction of the radius cut out in the center. 0 — a regular pie, 0.5 — a ring.</summary>
    public float HoleRatio { get; set; }

    public bool ShowLegend { get; set; } = true;
    public bool ShowPercentages { get; set; } = true;
    public float HoverOffset { get; set; } = 6f;

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

    /// <summary>The color behind the chart: its own background, otherwise that of
    /// the nearest opaque ancestor, otherwise the theme's. The ring's hole is painted
    /// with it — there is no "clip out" in Graphics, and the hole must look like
    /// an opening.</summary>
    /// <remarks>
    /// The hole used to be painted with the chart's own background, or white if it
    /// was transparent — which it is by default. On a dark page the ring got
    /// a white center.
    /// </remarks>
    private Color BackdropColor
    {
        get
        {
            for (UIElement? node = this; node is not null; node = node.Parent)
                if (node.Background.A == 255)
                    return node.Background;

            return App.Theme.Colors.Background;
        }
    }

    // the background, border and corner radius are drawn by the base — only the title, the slices and the legend here
    protected override void DrawContent(Graphics g)
    {
        DrawTitle(g);

        float total = Total;
        if (total <= 0 || Slices.Count == 0) return;

        Rectangle circle = PieBounds;
        float angle = -90f;   // start from 12 o'clock

        for (int i = 0; i < Slices.Count; i++)
        {
            float value = Math.Max(0, Slices[i].Value);
            float sweep = 360f * value / total;
            Color color = Slices[i].Color ?? ChartPalette.At(i);

            Rectangle sliceBounds = circle;

            if (i == _hoveredSlice)
            {
                // push the slice outward along its bisector
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
                BackdropColor);
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

    protected override void OnMouseMove(MouseMoveEventArgs args)
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
            // the angle from 12 o'clock clockwise, the same way the slices are drawn
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

    protected override void OnMouseExit(MouseMoveEventArgs args)
    {
        if (_hoveredSlice < 0) return;

        _hoveredSlice = -1;

        // without a redraw the pushed-out slice stayed out after the mouse left
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(new Size(260 + LegendWidth + Padding.Horizontal, 200 + Padding.Vertical), availableSize);
}