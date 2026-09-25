using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// The common basis of pressable controls: states, a color for each state
/// and the press ripple. The content is drawn by derived classes.
/// </summary>
public abstract partial class ButtonBase : InteractiveControl
{
    private readonly RippleAnimation _ripple;

    /// <summary>Show a ripple spreading from the press point.</summary>
    [Styled(Category = "Button")]
    public partial bool RippleEnabled { get; set; }

    private static bool RippleEnabledDefault => true;

    // the ripple has no field of its own on the button — the value is stored
    // by RippleAnimation, hence the manual SetValue overload without ref
    public static readonly StyledProperty<Color> RippleColorProperty =
        StyledProperty<Color>.Register<ButtonBase>(
            nameof(RippleColor),
            button => button._ripple.Color,
            (button, value) => button._ripple.Color = value,
            new Color(60, 255, 255, 255),
            category: "Button");

    public Color RippleColor
    {
        get => _ripple.Color;
        set => SetValue(RippleColorProperty, value);
    }

    [Styled(Category = "States")]
    public partial Color BackgroundColor { get; set; }

    [Styled(Category = "States")]
    public partial Color HoverBackgroundColor { get; set; }

    [Styled(Category = "States")]
    public partial Color PressedBackgroundColor { get; set; }

    [Styled(Category = "States")]
    public partial Color CheckedBackgroundColor { get; set; }

    [Styled(Category = "States")]
    public partial Color DisabledBackgroundColor { get; set; }

    [Styled(Category = "States")]
    public partial Color CheckedPressedBackgroundColor { get; set; }

    [Styled(Category = "States")]
    public partial Color CheckedHoverBackgroundColor { get; set; }

    [Styled(Category = "States")]
    public partial Color DisabledTextColor { get; set; }
    private static Color DisabledTextColorDefault => new(255, 160, 160, 160);

    [Styled(Category = "Button")]
    public partial Color FocusRingColor { get; set; }

    [Styled(Category = "Button")]
    public partial bool ShowFocusRing { get; set; }
    private static bool ShowFocusRingDefault => true;

    /// <summary>The latched state — for ToggleButton and the like.</summary>
    protected virtual bool IsCheckedState => false;

    protected ButtonBase()
    {
        _ripple = new RippleAnimation(this);

        Cursor = CursorKind.Hand;
        SetControlDefault(PaddingProperty, new(14, 6));
        SetControlDefault(CornerRadiusProperty, new CornerRadius(4f));
        SetControlDefault(BorderWidthProperty, 1f);
    }

    /// <summary>The backdrop color for the current state. The order of checks
    /// defines priority: disabled beats pressed, pressed beats hover.</summary>
    protected override Color CurrentBackground
    {
        get
        {
            if (!IsEnabled && DisabledBackgroundColor.A > 0)
                return DisabledBackgroundColor;

            // the latched state has its own shades for pressed and hover,
            // otherwise the button briefly repaints in the color of the unchecked state
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

    /// <summary>On a button, focus is shown by the ring in DrawDecoration.
    /// Swapping the border as well is a double signal: you get two rings
    /// two pixels apart.</summary>
    protected override Color CurrentBorderColor => BorderColor;

    protected virtual Color CurrentTextColor => IsEnabled ? TextColor : DisabledTextColor;

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (!RippleEnabled || e.Button != MouseButton.Left) return;

        Point abs = GetAbsolutePosition();
        _ripple.Start(new Point(e.Location.X - abs.X, e.Location.Y - abs.Y));
    }

    /// <summary>
    /// The ripple is drawn first, before the content: it must lie
    /// on the backdrop and under the text.
    /// </summary>
    protected sealed override void DrawContent(Graphics g)
    {
        _ripple.Draw(g, LocalBounds, CornerRadius);

        DrawButtonContent(g);
    }

    /// <summary>The button content on top of the backdrop and the ripple.</summary>
    protected abstract void DrawButtonContent(Graphics g);

    protected override void DrawDecoration(Graphics g)
    {
        if (!IsFocused || !ShowFocusRing || FocusRingColor.A == 0) return;

        Rectangle bounds = LocalBounds;

        // the ring sits slightly inside the bounds, otherwise the parent's clip cuts it off
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

    /// <summary>The control was pressed — click, Space or Enter.</summary>
    protected virtual void OnActivated() { }
}