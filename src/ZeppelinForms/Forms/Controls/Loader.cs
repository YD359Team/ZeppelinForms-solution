using ZeppelinForms.Animation;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// An indicator of an unfinished operation. The duration is unknown —
/// for a known one there is ProgressBar.
/// </summary>
public partial class Loader : DecoratedControl
{
    private const string AnimationKey = "loader-phase";

    private float _phase;

    public LoaderStyle Style
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // every style has its own desired size
            Invalidate();
        }
    } = LoaderStyle.Ring;

    [Styled(Category = "Appearance")]
    public partial Color Color { get; set; }
    private static Color ColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    /// <summary>The color of the track under the indicator. Transparent — don't draw it.</summary>
    [Styled(Category = "Appearance")]
    public partial Color TrackColor { get; set; }
    private static Color TrackColorDefault => Colors.Transparent;

    /// <summary>Line thickness. Not Thickness: that is the name of the padding type,
    /// and inside the class the name would hide it.</summary>
    public float StrokeWidth
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the Bar style takes its height from the stroke
            Invalidate();
        }
    } = 3f;

    /// <summary>The desired indicator size, not counting Padding.</summary>
    public float IndicatorSize
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = 32f;

    /// <summary>The duration of one revolution.</summary>
    public TimeSpan Period
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // a running loop keeps the period it was started with — restart it
            if (IsRunning && IsVisible) Start();
        }
    } = TimeSpan.FromMilliseconds(1200);

    /// <summary>The number of rays in Spinner and dots in Dots.</summary>
    public int ElementCount
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the Dots style takes its width from the count
            Invalidate();
        }
    } = 8;

    public bool IsRunning
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            if (value) Start();
            else Stop();
        }
    } = true;

    protected override void OnAttached()
    {
        if (IsRunning && IsVisible) Start();
    }

    // removing the animation in OnDetached is not needed: DetachTree throws out
    // everything whose Target is in the subtree being removed

    private void Start()
    {
        this.AnimateLoop(AnimationKey, Period, phase => _phase = phase);
    }

    private void Stop()
    {
        this.StopAnimation(AnimationKey);

        _phase = 0f;
        InvalidateVisual();
    }

    // the background, border and corner radius are drawn by the base
    protected override void DrawContent(Graphics g)
    {
        Rectangle content = ContentBounds;

        if (content.Width <= 0 || content.Height <= 0) return;

        switch (Style)
        {
            case LoaderStyle.Ring: DrawRing(g, content); break;
            case LoaderStyle.Spinner: DrawSpinner(g, content); break;
            case LoaderStyle.Dots: DrawDots(g, content); break;
            case LoaderStyle.Bar: DrawBar(g, content); break;
        }
    }

    private void DrawRing(Graphics g, Rectangle content)
    {
        float diameter = Math.Min(content.Width, content.Height) - StrokeWidth;
        if (diameter <= 0) return;

        var circle = new Rectangle(
            new Point(
                content.X + (content.Width - diameter) / 2f,
                content.Y + (content.Height - diameter) / 2f),
            new Size(diameter, diameter));

        if (TrackColor.A > 0)
            g.DrawEllipse(circle, TrackColor, StrokeWidth);

        // the arc length pulses from 30° to 270°, and its start makes two
        // revolutions per period — together that gives a "catching-up tail"
        float grow = (1f - MathF.Cos(_phase * MathF.Tau)) / 2f;
        float sweep = 30f + 240f * grow;
        float start = _phase * 720f - 90f;

        g.DrawArc(circle, start, sweep, Color, StrokeWidth);
    }

    private void DrawSpinner(Graphics g, Rectangle content)
    {
        int count = Math.Max(3, ElementCount);

        float radius = (Math.Min(content.Width, content.Height) - StrokeWidth) / 2f;
        if (radius <= 0) return;

        var center = new Point(
            content.X + content.Width / 2f,
            content.Y + content.Height / 2f);

        float inner = radius * 0.5f;

        // the leading ray is discrete: continuous rotation reads worse here
        // than clicks — the system indicator behaves the same way
        int head = (int)(_phase * count) % count;

        for (int i = 0; i < count; i++)
        {
            int behind = (head - i + count) % count;
            float fade = 1f - behind / (float)count;

            float angle = i / (float)count * MathF.Tau - MathF.PI / 2f;
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);

            g.DrawLine(
                new Point(center.X + cos * inner, center.Y + sin * inner),
                new Point(center.X + cos * radius, center.Y + sin * radius),
                new Color((byte)(Color.A * fade), Color.R, Color.G, Color.B),
                StrokeWidth);
        }
    }

    private void DrawDots(Graphics g, Rectangle content)
    {
        int count = Math.Max(2, ElementCount);

        float step = content.Width / count;
        float maxRadius = Math.Min(step, content.Height) / 2f;
        if (maxRadius <= 0) return;

        float centerY = content.Y + content.Height / 2f;

        for (int i = 0; i < count; i++)
        {
            // each dot lags behind the previous one by a fraction of the period
            float phase = _phase - i / (float)count;
            if (phase < 0f) phase += 1f;

            // a burst in the first half of its segment, rest in the second
            float pulse = phase < 0.5f ? MathF.Sin(phase * MathF.Tau) : 0f;

            float radius = maxRadius * (0.45f + 0.55f * pulse);
            float centerX = content.X + step * (i + 0.5f);

            g.FillEllipse(
                new Rectangle(
                    new Point(centerX - radius, centerY - radius),
                    new Size(radius * 2f, radius * 2f)),
                new Color((byte)(Color.A * (0.4f + 0.6f * pulse)), Color.R, Color.G, Color.B));
        }
    }

    private void DrawBar(Graphics g, Rectangle content)
    {
        float height = Math.Min(StrokeWidth * 2f, content.Height);
        var radius = new CornerRadius(height / 2f);

        var track = new Rectangle(
            new Point(content.X, content.Y + (content.Height - height) / 2f),
            new Size(content.Width, height));

        if (TrackColor.A > 0)
            g.FillRoundRectangle(track, radius, TrackColor);

        float segment = Math.Max(height, content.Width * 0.3f);

        // the segment enters and leaves beyond the edges, so the track clips it
        float x = track.X - segment + _phase * (track.Width + segment);

        g.Save();
        g.ClipRoundRect(track, radius);

        g.FillRoundRectangle(
            new Rectangle(new Point(x, track.Y), new Size(segment, height)),
            radius, Color);

        g.Restore();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size content = Style switch
        {
            // a row of dots is wider than it is tall
            LoaderStyle.Dots => new Size(IndicatorSize * Math.Max(2, ElementCount) / 2f, IndicatorSize / 2f),

            // the bar stretches along the width, it has none of its own
            LoaderStyle.Bar => new Size(float.IsFinite(availableSize.Width) ? availableSize.Width : IndicatorSize * 4f,
                                        StrokeWidth * 2f),

            _ => new Size(IndicatorSize, IndicatorSize),
        };

        return ResolveSize(
            new Size(content.Width + Padding.Horizontal, content.Height + Padding.Vertical),
            availableSize);
    }

    protected override void OnStyledPropertyChanged(StyledProperty property)
    {
        base.OnStyledPropertyChanged(property);

        if (property != IsVisibleProperty) return;

        // a hidden indicator must not hold the window's ticker: its animation
        // is endless, and it will never finish by itself
        if (IsVisible && IsRunning) Start();
        else this.StopAnimation(AnimationKey);
    }
}

public enum LoaderStyle
{
    /// <summary>An arc of variable length, rotating. Material, Windows 11.</summary>
    Ring,

    /// <summary>Rays around a circle with fading opacity. iOS, macOS.</summary>
    Spinner,

    /// <summary>A row of pulsing dots.</summary>
    Dots,

    /// <summary>A segment running along a track. Indeterminate progress.</summary>
    Bar,
}