using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Progress as an arc: a full circle or any part of it — a half circle, a sector.
/// </summary>
/// <remarks>
/// The half-circle variant is not a separate control but the same one
/// with a different arc: StartAngle 180 and SweepAngle 180. The layout changes
/// then: a half circle takes half as much height as width, and the circle's
/// center moves to the bottom edge.
/// </remarks>
public partial class CircularProgressBar : DecoratedControl
{
    /// <summary>The value as it was assigned, before coercing into the range.</summary>
    /// <remarks>
    /// Coercing on read makes the result independent of the order of assignments:
    /// in <c>new CircularProgressBar { Value = 150, Maximum = 200 }</c> the value
    /// used to be clamped by the default maximum of 100 before the real one arrived.
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

    public float ArcThickness
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the arc thickness is part of the circle's size, not only of drawing
            Invalidate();
        }
    } = 8f;

    public bool ShowPercentage
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = true;

    /// <summary>Where the arc starts: −90 — from 12 o'clock, 180 — from 9 o'clock.</summary>
    public float StartAngle
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = -90f;

    /// <summary>What part of the circle the scale takes. 360 — a full circle,
    /// 180 together with StartAngle = 180 — a half circle.</summary>
    public float SweepAngle
    {
        get;
        set
        {
            if (field == value) return;

            field = Math.Clamp(value, 1f, 360f);

            // the desired size of the control depends on how far the scale extends too
            Invalidate();
        }
    } = 360f;

    [Styled(Category = "Progress")]
    public partial Color FillColor { get; set; }
    private static Color FillColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    [Styled(Category = "Progress")]
    public partial Color TrackColor { get; set; }
    private static Color TrackColorDefault => new(255, 230, 230, 230);

    /// <summary>The scale takes half a circle or less: the layout is different
    /// then — height is half the width, the center is at the bottom.</summary>
    private bool IsHalf => SweepAngle <= 180f;

    private float Fraction
    {
        get
        {
            float range = Maximum - Minimum;
            return range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0f, 1f);
        }
    }

    protected override void DrawContent(Graphics g)
    {
        var content = this.ContentBounds;

        // the circle is fitted into a square by the smaller side, otherwise it would
        // be an ellipse. A half circle needs only half in height, so it is allowed
        // to be half as tall
        float available = IsHalf
            ? Math.Min(content.Width, content.Height * 2f)
            : Math.Min(content.Width, content.Height);

        float diameter = available - ArcThickness;
        if (diameter <= 0) return;

        float top = IsHalf
            ? content.Y + content.Height - diameter / 2f - ArcThickness / 2f
            : content.Y + (content.Height - diameter) / 2f;

        var circle = new Rectangle(
            new Point(content.X + (content.Width - diameter) / 2f, top),
            new Size(diameter, diameter));

        g.DrawArc(circle, StartAngle, SweepAngle, TrackColor, ArcThickness);

        float fraction = Fraction;
        if (fraction > 0)
            g.DrawArc(circle, StartAngle, SweepAngle * fraction, FillColor, ArcThickness);

        if (!ShowPercentage) return;

        // for a half circle the label sits inside the arc rather than in the center
        // of the control: its circle's center is on the bottom edge
        var textArea = IsHalf
            ? new Rectangle(
                new Point(circle.X, circle.Y + diameter / 2f - ArcThickness - LineHeight),
                new Size(diameter, LineHeight))
            : content;

        g.DrawText($"{fraction * 100:0}%", textArea, TextColor, EffectiveFont,
            HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
    }

    private float LineHeight => TextMeasurer.Current.MeasureText("0%", EffectiveFont).Height;

    protected override Size MeasureOverride(Size availableSize)
    {
        var content = IsHalf
            ? new Size(128 + Padding.Horizontal, 72 + Padding.Vertical)
            : new Size(64 + Padding.Horizontal, 64 + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }
}