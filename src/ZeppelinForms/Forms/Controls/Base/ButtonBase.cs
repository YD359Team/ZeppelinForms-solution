using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Общая основа нажимаемых контролов: состояния, цвета под каждое
/// состояние и волна нажатия. Содержимое рисуют наследники.
/// </summary>
public abstract class ButtonBase : InteractiveControl
{
    private readonly RippleAnimation _ripple;

    public static readonly StyledProperty<bool> RippleEnabledProperty =
    StyledProperty<bool>.Register<ButtonBase>(
        nameof(RippleEnabled),
        button => button._rippleEnabled,
        (button, value) => button._rippleEnabled = value,
        true,
        category: "Кнопка");

    // у волны нет своего поля на кнопке: значение хранит RippleAnimation
    public static readonly StyledProperty<Color> RippleColorProperty =
        StyledProperty<Color>.Register<ButtonBase>(
            nameof(RippleColor),
            button => button._ripple.Color,
            (button, value) => button._ripple.Color = value,
            new Color(60, 255, 255, 255),
            category: "Кнопка");

    public static readonly StyledProperty<Color> BackgroundColorProperty =
        StyledProperty<Color>.Register<ButtonBase>(
            nameof(BackgroundColor),
            button => button._backgroundColor,
            (button, value) => button._backgroundColor = value,
            Colors.Transparent,
            category: "Состояния");

    public static readonly StyledProperty<Color> HoverBackgroundColorProperty =
        StyledProperty<Color>.Register<ButtonBase>(
            nameof(HoverBackgroundColor),
            button => button._hoverBackgroundColor,
            (button, value) => button._hoverBackgroundColor = value,
            Colors.Transparent,
            category: "Состояния");

    public static readonly StyledProperty<Color> PressedBackgroundColorProperty =
        StyledProperty<Color>.Register<ButtonBase>(
            nameof(PressedBackgroundColor),
            button => button._pressedBackgroundColor,
            (button, value) => button._pressedBackgroundColor = value,
            Colors.Transparent,
            category: "Состояния");

    public static readonly StyledProperty<Color> CheckedBackgroundColorProperty =
        StyledProperty<Color>.Register<ButtonBase>(
            nameof(CheckedBackgroundColor),
            button => button._checkedBackgroundColor,
            (button, value) => button._checkedBackgroundColor = value,
            Colors.Transparent,
            category: "Состояния");

    public static readonly StyledProperty<Color> DisabledBackgroundColorProperty =
        StyledProperty<Color>.Register<ButtonBase>(
            nameof(DisabledBackgroundColor),
            button => button._disabledBackgroundColor,
            (button, value) => button._disabledBackgroundColor = value,
            Colors.Transparent,
            category: "Состояния");

    public static readonly StyledProperty<Color> CheckedPressedBackgroundColorProperty =
        StyledProperty<Color>.Register<ButtonBase>(
            nameof(CheckedPressedBackgroundColor),
            button => button._checkedPressedBackgroundColor,
            (button, value) => button._checkedPressedBackgroundColor = value,
            Colors.Transparent,
            category: "Состояния");

    public static readonly StyledProperty<Color> CheckedHoverBackgroundColorProperty =
        StyledProperty<Color>.Register<ButtonBase>(
            nameof(CheckedHoverBackgroundColor),
            button => button._checkedHoverBackgroundColor,
            (button, value) => button._checkedHoverBackgroundColor = value,
            Colors.Transparent,
            category: "Состояния");

    public static readonly StyledProperty<Color> DisabledTextColorProperty =
        StyledProperty<Color>.Register<ButtonBase>(
            nameof(DisabledTextColor),
            button => button._disabledTextColor,
            (button, value) => button._disabledTextColor = value,
            new Color(255, 160, 160, 160),
            category: "Состояния");

    public static readonly StyledProperty<Color> FocusRingColorProperty =
        StyledProperty<Color>.Register<ButtonBase>(
            nameof(FocusRingColor),
            button => button._focusRingColor,
            (button, value) => button._focusRingColor = value,
            Colors.Transparent,
            category: "Кнопка");

    public static readonly StyledProperty<bool> ShowFocusRingProperty =
        StyledProperty<bool>.Register<ButtonBase>(
            nameof(ShowFocusRing),
            button => button._showFocusRing,
            (button, value) => button._showFocusRing = value,
            true,
            category: "Кнопка");

    private bool _rippleEnabled = true;
    private Color _backgroundColor = Colors.Transparent;
    private Color _hoverBackgroundColor = Colors.Transparent;
    private Color _pressedBackgroundColor = Colors.Transparent;
    private Color _checkedBackgroundColor = Colors.Transparent;
    private Color _disabledBackgroundColor = Colors.Transparent;
    private Color _checkedPressedBackgroundColor = Colors.Transparent;
    private Color _checkedHoverBackgroundColor = Colors.Transparent;
    private Color _disabledTextColor = new(255, 160, 160, 160);
    private Color _focusRingColor = Colors.Transparent;
    private bool _showFocusRing = true;

    /// <summary>Показывать расходящуюся волну от точки нажатия.</summary>
    public bool RippleEnabled
    {
        get => _rippleEnabled;
        set => SetValue(RippleEnabledProperty, ref _rippleEnabled, value);
    }

    public Color RippleColor
    {
        get => _ripple.Color;
        set => SetValue(RippleColorProperty, value);
    }

    public Color BackgroundColor
    {
        get => _backgroundColor;
        set => SetValue(BackgroundColorProperty, ref _backgroundColor, value);
    }

    public Color HoverBackgroundColor
    {
        get => _hoverBackgroundColor;
        set => SetValue(HoverBackgroundColorProperty, ref _hoverBackgroundColor, value);
    }

    public Color PressedBackgroundColor
    {
        get => _pressedBackgroundColor;
        set => SetValue(PressedBackgroundColorProperty, ref _pressedBackgroundColor, value);
    }

    public Color CheckedBackgroundColor
    {
        get => _checkedBackgroundColor;
        set => SetValue(CheckedBackgroundColorProperty, ref _checkedBackgroundColor, value);
    }

    public Color DisabledBackgroundColor
    {
        get => _disabledBackgroundColor;
        set => SetValue(DisabledBackgroundColorProperty, ref _disabledBackgroundColor, value);
    }

    public Color CheckedPressedBackgroundColor
    {
        get => _checkedPressedBackgroundColor;
        set => SetValue(CheckedPressedBackgroundColorProperty, ref _checkedPressedBackgroundColor, value);
    }

    public Color CheckedHoverBackgroundColor
    {
        get => _checkedHoverBackgroundColor;
        set => SetValue(CheckedHoverBackgroundColorProperty, ref _checkedHoverBackgroundColor, value);
    }

    public Color DisabledTextColor
    {
        get => _disabledTextColor;
        set => SetValue(DisabledTextColorProperty, ref _disabledTextColor, value);
    }

    public Color FocusRingColor
    {
        get => _focusRingColor;
        set => SetValue(FocusRingColorProperty, ref _focusRingColor, value);
    }

    public bool ShowFocusRing
    {
        get => _showFocusRing;
        set => SetValue(ShowFocusRingProperty, ref _showFocusRing, value);
    }

    /// <summary>Залипшее состояние — для ToggleButton и подобных.</summary>
    protected virtual bool IsCheckedState => false;

    protected ButtonBase()
    {
        _ripple = new RippleAnimation(this);

        Cursor = CursorKind.Hand;
        Padding = new Thickness(14, 6);
        SetControlDefault(CornerRadiusProperty, new CornerRadius(4f));
        SetControlDefault(BorderWidthProperty, 1f);
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
