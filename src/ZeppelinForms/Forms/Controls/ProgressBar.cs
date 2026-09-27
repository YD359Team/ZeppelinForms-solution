using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

public partial class ProgressBar : DecoratedControl
{
    /// <summary>The value as it was assigned, before coercing into the range.</summary>
    /// <remarks>
    /// Coercing on read rather than on write makes the result independent of the
    /// order of assignments: in <c>new ProgressBar { Value = 150, Maximum = 200 }</c>
    /// the value used to be clamped by the default maximum of 100 before
    /// the real one arrived.
    /// </remarks>
    private float _requested;

    public float Minimum
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = 0f;

    public float Maximum
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = 100f;

    public float Value
    {
        get => Coerce(_requested);
        set
        {
            float before = Value;
            _requested = value;

            if (Math.Abs(before - Value) < 0.001f) return;

            InvalidateVisual();
        }
    }

    // Min/Max rather than Math.Clamp: while the range is being reassigned,
    // Minimum may briefly exceed Maximum, and Math.Clamp throws on that
    private float Coerce(float value) => Math.Min(Math.Max(value, Minimum), Maximum);

    public Orientation Orientation
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the default size swaps its axes together with the orientation
            Invalidate();
        }
    } = Orientation.Horizontal;

    public bool ShowPercentage
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    }

    /// <summary>Custom label formatting: the fraction (0..1) and the current value.</summary>
    public Func<float, float, string>? TextFormatter
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    }

    [Styled(Category = "Progress")]
    public partial Color FillColor { get; set; }
    private static Color FillColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    [Styled(Category = "Progress")]
    public partial Color TrackColor { get; set; }
    private static Color TrackColorDefault => new(255, 230, 230, 230);

    public Color FilledTextColor
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = Colors.White;

    public ProgressBar()
    {
        // a progress bar by its nature stretches along, rather than keeping its size
        SetControlDefault(HorizontalAlignmentProperty, Enums.HorizontalAlignment.Stretch);
        SetControlDefault(VerticalAlignmentProperty, Enums.VerticalAlignment.Center);
        SetControlDefault(BorderColorProperty, new Color(255, 180, 180, 180));
        SetControlDefault(BorderWidthProperty, 1f);
    }

    private float Fraction
    {
        get
        {
            float range = Maximum - Minimum;
            return range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0f, 1f);
        }
    }

    private string DisplayText
    {
        get
        {
            float fraction = Fraction;
            return TextFormatter?.Invoke(fraction, Value) ?? $"{fraction * 100:0}%";
        }
    }

    // the track is the bar's background, so it is replaced entirely
    protected override Color CurrentBackground => TrackColor;

    protected override void DrawContent(Graphics g)
    {
        Rectangle bounds = LocalBounds;
        float fraction = Fraction;

        if (fraction > 0)
        {
            Rectangle fill = Orientation == Orientation.Horizontal
                ? new Rectangle(bounds.Position, new Size(bounds.Width * fraction, bounds.Height))
                // a vertical one grows bottom-up, as the eye expects
                : new Rectangle(
                    new Point(bounds.X, bounds.Y + bounds.Height * (1 - fraction)),
                    new Size(bounds.Width, bounds.Height * fraction));

            g.FillRoundRectangle(fill, CornerRadius, FillColor);
        }

        if (!ShowPercentage) return;

        string label = DisplayText;

        // the same text in two colors: contrast is kept
        // both on the filled part and on the track
        g.Save();
        g.ClipRect(new Rectangle(bounds.Position, new Size(bounds.Width * fraction, bounds.Height)));
        g.DrawText(label, bounds, FilledTextColor, EffectiveFont,
            HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
        g.Restore();

        g.Save();
        g.ClipRect(new Rectangle(
            new Point(bounds.X + bounds.Width * fraction, bounds.Y),
            new Size(bounds.Width * (1 - fraction), bounds.Height)));
        g.DrawText(label, bounds, TextColor, EffectiveFont,
            HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
        g.Restore();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var content = Orientation == Orientation.Horizontal
            ? new Size(160 + Padding.Horizontal, 18 + Padding.Vertical)
            : new Size(18 + Padding.Horizontal, 160 + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }
}