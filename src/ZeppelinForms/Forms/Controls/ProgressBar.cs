using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

public class ProgressBar : UnitControl, IBorderedElement
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

    public Orientation Orientation { get; set; } = Orientation.Horizontal;
    public bool ShowPercentage { get; set; }

    /// <summary>Своё форматирование подписи. Получает долю (0..1) и текущее значение.</summary>
    public Func<float, float, string>? TextFormatter { get; set; }

    private string DisplayText
    {
        get
        {
            float fraction = Fraction;
            return TextFormatter?.Invoke(fraction, Value) ?? $"{fraction * 100:0}%";
        }
    }

    public Color FilledTextColor { get; set; } = Colors.White;
    public Color FillColor { get; set; } = LightThemeColors.ButtonFill;
    public Color TrackColor { get; set; } = new Color(255, 230, 230, 230);
    public Color TextColor { get; set; } = Colors.Black;

    public Color BorderColor { get; set; } = new Color(255, 180, 180, 180);
    public float BorderWidth { get; set; } = 1f;

    public ProgressBar()
    {
        // полоса прогресса по своей природе тянется вдоль, а не сохраняет размер
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Center;
    }

    private float Fraction
    {
        get
        {
            float range = Maximum - Minimum;
            return range <= 0 ? 0 : Math.Clamp((_value - Minimum) / range, 0f, 1f);
        }
    }

    public override void Draw(Graphics g)
    {
        var bounds = this.LocalBounds;

        g.FillRectangle(bounds, TrackColor);

        float fraction = Fraction;

        if (fraction > 0)
        {
            Rectangle fill = Orientation == Orientation.Horizontal
                ? new Rectangle(bounds.Position, new Size(bounds.Width * fraction, bounds.Height))
                // вертикальная растёт снизу вверх, как и ожидает глаз
                : new Rectangle(
                    new Point(bounds.X, bounds.Y + bounds.Height * (1 - fraction)),
                    new Size(bounds.Width, bounds.Height * fraction));

            g.FillRectangle(fill, FillColor);
        }

        if (BorderWidth > 0)
            g.DrawRectangle(bounds, BorderColor, BorderWidth);

        if (ShowPercentage)
        {
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
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var content = Orientation == Orientation.Horizontal
            ? new Size(160 + Padding.Horizontal, 18 + Padding.Vertical)
            : new Size(18 + Padding.Horizontal, 160 + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }
}