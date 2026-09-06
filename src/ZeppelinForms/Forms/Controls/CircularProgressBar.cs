using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls;

public class CircularProgressBar : DecoratedControl
{
    private float _value;

    public float Minimum { get; set; } = 0f;
    public float Maximum { get; set; } = 100f;

    public float Value
    {
        get => _value;
        set
        {
            float clamped = Math.Clamp(value, Minimum, Maximum);
            if (Math.Abs(_value - clamped) < 0.001f) return;

            _value = clamped;
            Invalidate();
        }
    }

    public float ArcThickness { get; set; } = 8f;
    public bool ShowPercentage { get; set; } = true;

    /// <summary>Откуда начинать дугу: −90 — с 12 часов.</summary>
    public float StartAngle { get; set; } = -90f;

    public Color FillColor { get; set; } = new Color(255, 0x0D, 0x6E, 0xFD);
    public Color TrackColor { get; set; } = new Color(255, 230, 230, 230);

    private float Fraction
    {
        get
        {
            float range = Maximum - Minimum;
            return range <= 0 ? 0 : Math.Clamp((_value - Minimum) / range, 0f, 1f);
        }
    }

    protected override void DrawContent(Graphics g)
    {
        var content = this.ContentBounds;

        // круг вписываем в квадрат по меньшей стороне, иначе получится эллипс
        float diameter = Math.Min(content.Width, content.Height) - ArcThickness;
        if (diameter <= 0) return;

        var circle = new Rectangle(
            new Point(
                content.X + (content.Width - diameter) / 2f,
                content.Y + (content.Height - diameter) / 2f),
            new Size(diameter, diameter));

        g.DrawArc(circle, 0, 360, TrackColor, ArcThickness);

        float fraction = Fraction;
        if (fraction > 0)
            g.DrawArc(circle, StartAngle, 360f * fraction, FillColor, ArcThickness);

        if (ShowPercentage)
            g.DrawText($"{fraction * 100:0}%", content, TextColor, EffectiveFont,
                HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var content = new Size(64 + Padding.Horizontal, 64 + Padding.Vertical);
        return ResolveSize(content, availableSize);
    }
}