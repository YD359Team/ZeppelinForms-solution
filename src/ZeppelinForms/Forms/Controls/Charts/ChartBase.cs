using System.Collections.Specialized;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Charts;

/// <summary>
/// Common to all charts: background, border, title, label color.
/// Axes, grid and the value range live in <see cref="CartesianChartBase"/> —
/// a pie chart doesn't need them.
/// </summary>
public abstract partial class ChartBase : DecoratedControl
{
    public string? Title
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the title takes a strip at the top, and the plot area shrinks by it
            InvalidateVisual();
        }
    }

    /// <summary>The color of axis labels, categories and the legend.
    /// A styled property, so the theme sets it.</summary>
    [Styled(Category = "Chart")]
    public partial Color LabelColor { get; set; }
    private static Color LabelColorDefault => new(255, 90, 90, 90);

    [Styled(Category = "Chart")]
    public partial Color TitleColor { get; set; }
    private static Color TitleColorDefault => Colors.Black;

    /// <summary>The band behind what the pointer is over: a candle, a category of bars
    /// or of lines. Translucent, so the theme gives it the tint of its text.</summary>
    /// <remarks>Since 0.19.2 a property of every chart; it was the candlestick chart's.</remarks>
    [Styled(Category = "Chart")]
    public partial Color HoverColor { get; set; }
    private static Color HoverColorDefault => new(24, 0, 0, 0);

    /// <summary>The backing of the readout — the values of what the pointer is over.</summary>
    /// <remarks>Since 0.19.2 a property of every chart; it was the candlestick chart's.</remarks>
    [Styled(Category = "Chart")]
    public partial Color ReadoutBackground { get; set; }
    private static Color ReadoutBackgroundDefault => new(220, 255, 255, 255);

    /// <summary>Show the values of what the pointer is over: a candle's prices, the
    /// values of a category's series, a function's value at the pointer.</summary>
    public bool ShowReadout { get; set; } = true;

    protected float TitleHeight => string.IsNullOrEmpty(Title)
        ? 0
        : TextMeasurer.LineHeight(EffectiveFont) + 8f;

    /// <summary>A chart has no natural size — the plot takes whatever room it is
    /// given — so it fills its space, as a view onto content does (see
    /// <see cref="UnitControl"/>). It used to stand centered at 280×180 in a Grid
    /// cell of any size. A chart with an explicit Size keeps it.</summary>
    protected ChartBase()
    {
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        SetControlDefault(VerticalAlignmentProperty, VerticalAlignment.Stretch);
    }

    protected void DrawTitle(Graphics g)
    {
        if (string.IsNullOrEmpty(Title)) return;

        var content = ContentBounds;

        g.DrawText(Title,
            new Rectangle(content.Position, new Size(content.Width, TitleHeight)),
            TitleColor, EffectiveFont,
            HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(new Size(280 + Padding.Horizontal, 180 + Padding.Vertical), availableSize);

    /// <summary>The pointer in the chart's own coordinates.</summary>
    private protected Point LocalPointer(Point location)
    {
        Point origin = GetAbsolutePosition();

        return new Point(location.X - origin.X, location.Y - origin.Y);
    }

    /// <summary>
    /// A readout box: a header and a row per series — a swatch of the series' color, its
    /// name and value —, beside <paramref name="anchorX"/> and kept inside
    /// <paramref name="bounds"/>.
    /// </summary>
    /// <remarks>
    /// It stands to the right of the anchor and goes to its left when it doesn't fit, as a
    /// tooltip does: a fixed corner, as the candlestick chart's, covers the very data a
    /// chart of several series is read by, and the eye has to travel to it and back.
    /// </remarks>
    private protected void DrawReadout(
        Graphics g, Rectangle bounds, float anchorX, float anchorY,
        string? header, IReadOnlyList<(string Text, Color Color)> rows)
    {
        if (!ShowReadout || (string.IsNullOrEmpty(header) && rows.Count == 0)) return;

        const float Padding = 6f;
        const float Swatch = 8f;
        const float SwatchGap = 6f;
        const float Offset = 12f;

        float lineHeight = TextMeasurer.LineHeight(EffectiveFont);
        bool hasHeader = !string.IsNullOrEmpty(header);

        float width = hasHeader ? TextMeasurer.MeasureLine(header, EffectiveFont).Width : 0f;

        foreach ((string text, _) in rows)
            width = Math.Max(width, Swatch + SwatchGap + TextMeasurer.MeasureLine(text, EffectiveFont).Width);

        int lines = rows.Count + (hasHeader ? 1 : 0);
        var size = new Size(width + Padding * 2, lineHeight * lines + Padding * 2);

        float x = anchorX + Offset;

        if (x + size.Width > bounds.X + bounds.Width - 2f)
            x = anchorX - Offset - size.Width;

        x = Math.Max(bounds.X + 2f, x);

        float y = Math.Clamp(anchorY, bounds.Y + 2f, Math.Max(bounds.Y + 2f, bounds.Y + bounds.Height - size.Height - 2f));

        var box = new Rectangle(new Point(x, y), size);
        g.FillRoundRectangle(box, new CornerRadius(4f), ReadoutBackground);

        float lineY = y + Padding;

        if (hasHeader)
        {
            g.DrawText(header!, new Rectangle(new Point(x + Padding, lineY), new Size(width, lineHeight)),
                LabelColor, EffectiveFont, HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

            lineY += lineHeight;
        }

        foreach ((string text, Color color) in rows)
        {
            g.FillRoundRectangle(
                new Rectangle(new Point(x + Padding, lineY + (lineHeight - Swatch) / 2f), new Size(Swatch, Swatch)),
                new CornerRadius(2f), color);

            g.DrawText(text,
                new Rectangle(new Point(x + Padding + Swatch + SwatchGap, lineY), new Size(width - Swatch - SwatchGap, lineHeight)),
                TitleColor, EffectiveFont, HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

            lineY += lineHeight;
        }
    }

    /// <summary>A point marking a hovered value: larger than the others, with a ring
    /// of the backing color, so it stands out on the line it lies on.</summary>
    private protected void DrawHoveredPoint(Graphics g, Point at, float radius, Color color)
    {
        float outer = radius + 3f;

        g.FillEllipse(new Rectangle(new Point(at.X - outer, at.Y - outer), new Size(outer * 2, outer * 2)), ReadoutBackground.WithA(255));
        g.FillEllipse(new Rectangle(new Point(at.X - radius - 1.5f, at.Y - radius - 1.5f), new Size((radius + 1.5f) * 2, (radius + 1.5f) * 2)), color);
    }

    // ===== the data =====

    /// <summary>The chart's data changed — a value, a category, a series, a slice: the
    /// pointer's place is forgotten, it may point past the data now, and the chart is
    /// drawn again.</summary>
    private protected void OnDataChanged()
    {
        ForgetHover();

        // a legend measured by its labels changes the chart's size
        if (MeasuresData) Invalidate();
        else InvalidateVisual();
    }

    /// <summary>The chart measures by its data: a legend as wide as its widest label.</summary>
    private protected virtual bool MeasuresData => false;

    /// <summary>Forget what the pointer is over: an index into the data.</summary>
    private protected virtual void ForgetHover()
    {
    }

    /// <summary>A collection of data of the chart changed.</summary>
    private protected void OnDataCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnDataChanged();

    /// <summary>The series the chart has: a value added to one shows as a series added to
    /// the chart does.</summary>
    private HashSet<ChartSeries>? _watchedSeries;

    /// <summary>The chart listens to its series: while it is in a form. A series is often
    /// the view model's and outlives the chart; listened to for good, it kept every chart
    /// it was ever shown in.</summary>
    private bool _listeningToSeries;

    /// <summary>The chart's series changed: it listens to the ones it has now and to no
    /// others, and is drawn again.</summary>
    private protected void WatchSeries(IEnumerable<ChartSeries> series)
    {
        var now = new HashSet<ChartSeries>(series, ReferenceEqualityComparer.Instance);

        if (_listeningToSeries)
        {
            if (_watchedSeries is not null)
                foreach (ChartSeries gone in _watchedSeries)
                    if (!now.Contains(gone)) gone.Changed -= OnDataChanged;

            foreach (ChartSeries added in now)
                if (_watchedSeries is null || !_watchedSeries.Contains(added)) added.Changed += OnDataChanged;
        }

        _watchedSeries = now;

        OnDataChanged();
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        if (_listeningToSeries) return;

        _listeningToSeries = true;

        if (_watchedSeries is not null)
            foreach (ChartSeries series in _watchedSeries)
                series.Changed += OnDataChanged;

        // what changed while nobody listened
        OnDataChanged();
    }

    protected override void OnDetached()
    {
        if (_listeningToSeries)
        {
            _listeningToSeries = false;

            if (_watchedSeries is not null)
                foreach (ChartSeries series in _watchedSeries)
                    series.Changed -= OnDataChanged;
        }

        base.OnDetached();
    }

    /// <summary>A series' name in a readout: its own, or its number.</summary>
    private protected static string SeriesName(ChartSeries series, int index) =>
        string.IsNullOrEmpty(series.Name) ? $"#{index + 1}" : series.Name;
}