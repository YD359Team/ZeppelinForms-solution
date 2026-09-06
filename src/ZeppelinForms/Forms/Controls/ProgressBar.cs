using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

public class ProgressBar : DecoratedControl
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
            InvalidateVisual();
        }
    }

    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    public bool ShowPercentage { get; set; }

    /// <summary>Своё форматирование подписи: доля (0..1) и текущее значение.</summary>
    public Func<float, float, string>? TextFormatter { get; set; }

    public Color FillColor { get; set; } = new Color(255, 0x0D, 0x6E, 0xFD);
    public Color TrackColor { get; set; } = new Color(255, 230, 230, 230);
    public Color FilledTextColor { get; set; } = Colors.White;

    public ProgressBar()
    {
        // полоса прогресса по своей природе тянется вдоль, а не сохраняет размер
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Center;

        SetControlDefault(BorderColorProperty, new Color(255, 180, 180, 180));
        SetControlDefault(BorderWidthProperty, 1f);
    }

    private float Fraction
    {
        get
        {
            float range = Maximum - Minimum;
            return range <= 0 ? 0 : Math.Clamp((_value - Minimum) / range, 0f, 1f);
        }
    }

    private string DisplayText
    {
        get
        {
            float fraction = Fraction;
            return TextFormatter?.Invoke(fraction, _value) ?? $"{fraction * 100:0}%";
        }
    }

    // дорожка — это фон полосы, поэтому подменяем его целиком
    protected override Color CurrentBackground => TrackColor;

    protected override void DrawContent(Graphics g)
    {
        Rectangle bounds = LocalBounds;
        float fraction = Fraction;

        if (fraction > 0)
        {
            Rectangle fill = Orientation == Orientation.Horizontal
                ? new Rectangle(bounds.Position, new Size(bounds.Width * fraction, bounds.Height))
                // вертикальная растёт снизу вверх, как и ожидает глаз
                : new Rectangle(
                    new Point(bounds.X, bounds.Y + bounds.Height * (1 - fraction)),
                    new Size(bounds.Width, bounds.Height * fraction));

            g.FillRoundRectangle(fill, CornerRadius, FillColor);
        }

        if (!ShowPercentage) return;

        string label = DisplayText;

        // тот же текст двумя цветами: контраст сохраняется
        // и на заполненной части, и на дорожке
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