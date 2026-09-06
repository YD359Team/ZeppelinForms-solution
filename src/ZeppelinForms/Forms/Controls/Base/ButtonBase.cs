using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Общая основа нажимаемых контролов: состояния, цвета под каждое
/// состояние и волна нажатия. Содержимое рисуют наследники.
/// </summary>
public abstract class ButtonBase : InteractiveControl
{
    private readonly RippleAnimation _ripple;

    /// <summary>Показывать расходящуюся волну от точки нажатия.</summary>
    public bool RippleEnabled { get; set; } = true;

    public Color RippleColor
    {
        get => _ripple.Color;
        set => _ripple.Color = value;
    }

    public Color BackgroundColor { get; set; } = Colors.Transparent;
    public Color HoverBackgroundColor { get; set; } = Colors.Transparent;
    public Color PressedBackgroundColor { get; set; } = Colors.Transparent;
    public Color CheckedBackgroundColor { get; set; } = Colors.Transparent;
    public Color DisabledBackgroundColor { get; set; } = Colors.Transparent;
    public Color CheckedPressedBackgroundColor { get; set; } = Colors.Transparent;
    public Color CheckedHoverBackgroundColor { get; set; } = Colors.Transparent;

    public Color DisabledTextColor { get; set; } = new Color(255, 160, 160, 160);

    public Color FocusRingColor { get; set; } = Colors.Transparent;
    public bool ShowFocusRing { get; set; } = true;

    /// <summary>Залипшее состояние — для ToggleButton и подобных.</summary>
    protected virtual bool IsCheckedState => false;

    protected ButtonBase()
    {
        Cursor = CursorKind.Hand;
        Padding = new Thickness(14, 6);
        CornerRadius = new CornerRadius(4f);
        BorderWidth = 1f;

        _ripple = new RippleAnimation(this);
    }

    /// <summary>Цвет подложки под текущее состояние. Порядок проверок
    /// определяет приоритет: выключено важнее нажатия, нажатие важнее наведения.</summary>
    protected override Color CurrentBackground
    {
        get
        {
            if (!IsEnabled && DisabledBackgroundColor.A > 0)
                return DisabledBackgroundColor;

            // для залипшего состояния нажатие и наведение — свои оттенки,
            // иначе кнопка на мгновение перекрашивается в цвет выключенного
            if (IsCheckedState)
            {
                if (IsPressed && CheckedPressedBackgroundColor.A > 0) return CheckedPressedBackgroundColor;
                if (IsHovered && CheckedHoverBackgroundColor.A > 0) return CheckedHoverBackgroundColor;

                return CheckedBackgroundColor;
            }

            if (IsPressed && PressedBackgroundColor.A > 0) return PressedBackgroundColor;
            if (IsHovered && HoverBackgroundColor.A > 0) return HoverBackgroundColor;

            return BackgroundColor;
        }
    }

    /// <summary>Фокус у кнопки показывает кольцо в DrawDecoration.
    /// Подменять ещё и рамку — двойной сигнал: получаются два кольца
    /// в двух пикселях друг от друга.</summary>
    protected override Color CurrentBorderColor => BorderColor;

    protected virtual Color CurrentTextColor => IsEnabled ? TextColor : DisabledTextColor;

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (!RippleEnabled || e.Button != MouseButton.Left) return;

        Point abs = GetAbsolutePosition();
        _ripple.Start(new Point(e.Location.X - abs.X, e.Location.Y - abs.Y));
    }

    /// <summary>
    /// Волна рисуется первой, до содержимого: она должна лежать
    /// на подложке и под текстом.
    /// </summary>
    protected sealed override void DrawContent(Graphics g)
    {
        _ripple.Draw(g, LocalBounds, CornerRadius);

        DrawButtonContent(g);
    }

    /// <summary>Содержимое кнопки поверх подложки и волны.</summary>
    protected abstract void DrawButtonContent(Graphics g);

    protected override void DrawDecoration(Graphics g)
    {
        if (!IsFocused || !ShowFocusRing || FocusRingColor.A == 0) return;

        Rectangle bounds = LocalBounds;

        // кольцо чуть внутри границ, иначе обрежется клипом родителя
        var ring = new Rectangle(
            new Point(bounds.X + 2, bounds.Y + 2),
            new Size(Math.Max(0, bounds.Width - 4), Math.Max(0, bounds.Height - 4)));

        g.DrawRoundRectangle(ring, CornerRadius, FocusRingColor, 1f);
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        if (!IsEnabled) return;

        OnActivated();
        e.Handled = true;
    }

    /// <summary>Контрол нажали — клик, пробел или Enter.</summary>
    protected virtual void OnActivated() { }
}
