using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Общая основа нажимаемых контролов: состояния, цвета под каждое
/// состояние и отрисовка подложки. Содержимое рисуют наследники.
/// </summary>
public abstract class ButtonBase : UnitControl, IInputElement, IBorderedElement
{
    public Color BackgroundColor { get; set; } = Colors.Transparent;
    public Color HoverBackgroundColor { get; set; } = Colors.Transparent;
    public Color PressedBackgroundColor { get; set; } = Colors.Transparent;
    public Color CheckedBackgroundColor { get; set; } = Colors.Transparent;
    public Color DisabledBackgroundColor { get; set; } = Colors.Transparent;

    public Color TextColor { get; set; } = Colors.Black;
    public Color DisabledTextColor { get; set; } = new Color(255, 160, 160, 160);

    public Color BorderColor { get; set; } = Colors.Transparent;
    public float BorderWidth { get; set; } = 1f;

    public Color FocusRingColor { get; set; } = Colors.Transparent;
    public bool ShowFocusRing { get; set; } = true;

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    protected override bool IsKeyActivatable => true;

    /// <summary>Залипшее состояние — для ToggleButton и подобных.</summary>
    protected virtual bool IsCheckedState => false;

    protected ButtonBase()
    {
        Cursor = CursorKind.Hand;
        Padding = new Thickness(14, 6);
        CornerRadius = new CornerRadius(4f);
    }

    /// <summary>Цвет подложки под текущее состояние. Порядок проверок
    /// определяет приоритет: выключено важнее нажатия, нажатие важнее наведения.</summary>
    protected virtual Color CurrentBackground
    {
        get
        {
            if (!IsEnabled && DisabledBackgroundColor.A > 0) return DisabledBackgroundColor;
            if (IsPressed && PressedBackgroundColor.A > 0) return PressedBackgroundColor;
            if (IsCheckedState && CheckedBackgroundColor.A > 0) return CheckedBackgroundColor;
            if (IsHovered && HoverBackgroundColor.A > 0) return HoverBackgroundColor;

            return BackgroundColor;
        }
    }

    protected virtual Color CurrentTextColor => IsEnabled ? TextColor : DisabledTextColor;

    public sealed override void Draw(Graphics g)
    {
        Rectangle bounds = LocalBounds;
        Color background = CurrentBackground;

        if (background.A > 0)
            g.FillRoundRectangle(bounds, CornerRadius, background);

        if (BorderWidth > 0 && BorderColor.A > 0)
            g.DrawRoundRectangle(bounds, CornerRadius, BorderColor, BorderWidth);

        DrawContent(g);

        // кольцо фокуса поверх содержимого, чуть внутри границ —
        // иначе оно обрежется клипом родителя
        if (IsFocused && ShowFocusRing && FocusRingColor.A > 0)
        {
            Rectangle ring = new(
                new Point(bounds.X + 2, bounds.Y + 2),
                new Size(Math.Max(0, bounds.Width - 4), Math.Max(0, bounds.Height - 4)));

            g.DrawRoundRectangle(ring, CornerRadius, FocusRingColor, 1.5f);
        }
    }

    /// <summary>Нарисовать содержимое поверх подложки.</summary>
    protected abstract void DrawContent(Graphics g);

    protected override void OnClick(MouseClickEventArgs e)
    {
        if (!IsEnabled) return;

        OnActivated();
        e.Handled = true;
    }

    /// <summary>Контрол нажали — клик, пробел или Enter.</summary>
    protected virtual void OnActivated() { }
}
