using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>Контрол с фоном, рамкой и скруглением. Рисует оформление сам,
/// наследники добавляют только содержимое.</summary>
public abstract class DecoratedControl : UnitControl, IBorderedElement
{
    public static readonly StyledProperty<Color> BorderColorProperty =
        StyledProperty<Color>.Register<DecoratedControl>(
            nameof(BorderColor),
            control => control._borderColor,
            (control, value) => control._borderColor = value,
            Colors.Transparent,
            category: "Оформление");

    public static readonly StyledProperty<float> BorderWidthProperty =
        StyledProperty<float>.Register<DecoratedControl>(
            nameof(BorderWidth),
            control => control._borderWidth,
            (control, value) => control._borderWidth = value,
            0f,
            category: "Оформление");

    private Color _borderColor = Colors.Transparent;
    private float _borderWidth;

    public Color BorderColor
    {
        get => _borderColor;
        set => SetValue(BorderColorProperty, ref _borderColor, value);
    }

    public float BorderWidth
    {
        get => _borderWidth;
        set => SetValue(BorderWidthProperty, ref _borderWidth, value);
    }

    /// <summary>Цвет фона под текущее состояние. Наследники переопределяют,
    /// если фон зависит от наведения или нажатия.</summary>
    protected virtual Color CurrentBackground => Background;

    protected virtual Color CurrentBorderColor => BorderColor;

    public sealed override void Draw(Graphics g)
    {
        Rectangle bounds = LocalBounds;

        if (CurrentBackground.A > 0)
            g.FillRoundRectangle(bounds, CornerRadius, CurrentBackground);

        DrawContent(g);

        // рамка поверх содержимого: иначе длинный текст её перекроет
        if (BorderWidth > 0 && CurrentBorderColor.A > 0)
            g.DrawRoundRectangle(bounds, CornerRadius, CurrentBorderColor, BorderWidth);

        DrawDecoration(g);
    }

    protected abstract void DrawContent(Graphics g);

    /// <summary>Поверх рамки — кольцо фокуса, индикаторы, полосы.</summary>
    protected virtual void DrawDecoration(Graphics g) { }
}
