using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Charts;

/// <summary>
/// A stock chart: a candle per period, a wick from the low to the high
/// and a body between the open and the close.
/// </summary>
/// <remarks>
/// The value axis here doesn't extend down to zero, unlike bars: prices live in
/// a narrow corridor, and the zero mark would flatten the whole picture into a strip
/// under the top edge. Instead the range is taken from the data with a small margin
/// above and below, so that the outermost wicks don't touch the border.
/// </remarks>
public partial class CandlestickChart : CartesianChartBase
{
    private int _hovered = -1;

    public List<Candle> Candles { get; init; } = [];

    public Color BullColor { get; set; } = new(255, 0x19, 0x87, 0x54);
    public Color BearColor { get; set; } = new(255, 0xDC, 0x35, 0x45);

    /// <summary>The band behind the hovered candle. Was a fixed translucent black,
    /// invisible on a dark background.</summary>
    [Styled(Category = "Chart")]
    public partial Color HoverColor { get; set; }
    private static Color HoverColorDefault => new(24, 0, 0, 0);

    /// <summary>The backing of the price readout. Was a fixed translucent white,
    /// a bright patch on a dark background.</summary>
    [Styled(Category = "Chart")]
    public partial Color ReadoutBackground { get; set; }
    private static Color ReadoutBackgroundDefault => new(220, 255, 255, 255);

    /// <summary>Draw rising candles hollow — that is how stock terminals
    /// usually show them.</summary>
    public bool HollowBullish { get; set; }

    /// <summary>The fraction of the step the candle body takes.</summary>
    public float BodyRatio { get; set; } = 0.62f;

    public float WickWidth { get; set; } = 1.4f;

    /// <summary>The margin at the edges of the scale, as a fraction of the price range.</summary>
    public float RangePadding { get; set; } = 0.06f;

    /// <summary>Show the prices of the hovered candle in the corner of the plot.</summary>
    public bool ShowReadout { get; set; } = true;

    // a price is never zero: the range is computed from the data itself
    protected override bool IncludeZero => false;

    protected override (float Min, float Max) DataRange
    {
        get
        {
            if (Candles.Count == 0) return (0f, 1f);

            float min = float.MaxValue;
            float max = float.MinValue;

            foreach (Candle candle in Candles)
            {
                min = Math.Min(min, candle.Low);
                max = Math.Max(max, candle.High);
            }

            float padding = Math.Max((max - min) * RangePadding, 0.0001f);

            return (min - padding, max + padding);
        }
    }

    private float Step
    {
        get
        {
            Rectangle plot = PlotArea;

            return Candles.Count == 0 ? 0 : plot.Width / Candles.Count;
        }
    }

    protected override void DrawContent(Graphics g)
    {
        DrawTitle(g);
        DrawValueAxis(g);

        if (Candles.Count == 0) return;

        Rectangle plot = PlotArea;
        float step = Step;
        float body = Math.Max(1f, step * Math.Clamp(BodyRatio, 0.1f, 1f));

        for (int i = 0; i < Candles.Count; i++)
        {
            Candle candle = Candles[i];
            Color color = candle.IsBullish ? BullColor : BearColor;

            float center = plot.X + step * (i + 0.5f);

            float high = ValueToY(candle.High);
            float low = ValueToY(candle.Low);
            float open = ValueToY(candle.Open);
            float close = ValueToY(candle.Close);

            // the hovered candle is highlighted with a band over the whole height
            // of the plot: that way it's clear which period the price readout belongs to
            if (i == _hovered)
                g.FillRectangle(
                    new Rectangle(new Point(center - step / 2f, plot.Y), new Size(step, plot.Height)),
                    HoverColor);

            g.DrawLine(new Point(center, high), new Point(center, low), color, WickWidth);

            float top = Math.Min(open, close);
            float height = Math.Max(1f, Math.Abs(close - open));

            var bodyRect = new Rectangle(new Point(center - body / 2f, top), new Size(body, height));

            if (HollowBullish && candle.IsBullish)
                g.DrawRectangle(bodyRect, color, 1.4f);
            else
                g.FillRectangle(bodyRect, color);
        }

        DrawPeriodLabels(g, plot, step);

        if (ShowReadout && _hovered >= 0 && _hovered < Candles.Count)
            DrawReadout(g, plot, Candles[_hovered]);
    }

    /// <summary>Period labels. Not all of them are drawn: there are always more
    /// labels than fit, and a stuck-together row reads worse than a sparse one.</summary>
    private void DrawPeriodLabels(Graphics g, Rectangle plot, float step)
    {
        float widest = 0;

        foreach (Candle candle in Candles)
            widest = Math.Max(widest,
                TextMeasurer.Current.MeasureText(candle.Label ?? string.Empty, EffectiveFont).Width);

        if (widest <= 0) return;

        int every = Math.Max(1, (int)MathF.Ceiling((widest + 8f) / Math.Max(step, 1f)));

        for (int i = 0; i < Candles.Count; i += every)
        {
            string? label = Candles[i].Label;
            if (string.IsNullOrEmpty(label)) continue;

            var rect = new Rectangle(
                new Point(plot.X + step * i, plot.Y + plot.Height + AxisLabelGap),
                new Size(step * every, LabelHeight));

            g.DrawText(label, rect, LabelColor, EffectiveFont,
                HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
        }
    }

    private void DrawReadout(Graphics g, Rectangle plot, Candle candle)
    {
        string text =
            $"{candle.Label} O {FormatValue(candle.Open)} H {FormatValue(candle.High)} " +
            $"L {FormatValue(candle.Low)} C {FormatValue(candle.Close)}";

        Size size = TextMeasurer.Current.MeasureText(text, EffectiveFont);

        var box = new Rectangle(
            new Point(plot.X + 6f, plot.Y + 6f),
            new Size(size.Width + 12f, size.Height + 8f));

        g.FillRoundRectangle(box, new CornerRadius(4f), ReadoutBackground);

        g.DrawText(text, box, candle.IsBullish ? BullColor : BearColor, EffectiveFont,
            HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
    }

    protected override void OnMouseMove(MouseMoveEventArgs args)
    {
        Point abs = GetAbsolutePosition();
        Rectangle plot = PlotArea;

        float x = args.Location.X - abs.X;
        float y = args.Location.Y - abs.Y;

        int index = -1;

        if (Candles.Count > 0 && Step > 0 &&
            x >= plot.X && x <= plot.X + plot.Width &&
            y >= plot.Y && y <= plot.Y + plot.Height)
            index = Math.Clamp((int)((x - plot.X) / Step), 0, Candles.Count - 1);

        if (index == _hovered) return;

        _hovered = index;
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs args)
    {
        if (_hovered < 0) return;

        _hovered = -1;
        InvalidateVisual();
    }
}