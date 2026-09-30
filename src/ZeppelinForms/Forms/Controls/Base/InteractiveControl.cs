using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Base;

// Base/InteractiveControl.cs
/// <summary>A decorated control that takes part in focus and input.</summary>
public abstract partial class InteractiveControl : DecoratedControl, IInputElement
{
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    [Styled(Category = "Appearance")]
    public partial Color FocusBorderColor { get; set; }
    private static Color FocusBorderColorDefault => Colors.Transparent;

    /// <summary>The border under the cursor. Tells the user the control can be
    /// clicked before they click it. Transparent — no hover change.</summary>
    [Styled(Category = "Appearance")]
    public partial Color HoverBorderColor { get; set; }
    private static Color HoverBorderColorDefault => Colors.Transparent;

    // ===== focus ring =====
    //
    // Moved here from ButtonBase: check boxes, radio buttons and switches need
    // a ring as much as buttons do, and each of them drew its own — or none.
    // The ring is drawn only while focus is visible, see UIElement.IsFocusVisible.

    /// <summary>The outer stroke of the focus ring. Transparent — the control's
    /// own fallback color, if it has one, otherwise no ring.</summary>
    [Styled(Category = "Focus")]
    public partial Color FocusRingColor { get; set; }

    /// <summary>The inner stroke, between the outer one and the control. Fluent
    /// uses it to separate the ring from any fill it lands on. Transparent — none.</summary>
    [Styled(Category = "Focus")]
    public partial Color FocusRingInnerColor { get; set; }
    private static Color FocusRingInnerColorDefault => Colors.Transparent;

    [Styled(Category = "Focus")]
    public partial float FocusRingThickness { get; set; }
    private static float FocusRingThicknessDefault => 1f;

    /// <summary>The thickness of the inner stroke. Zero — a ring of one line.</summary>
    [Styled(Category = "Focus")]
    public partial float FocusRingInnerThickness { get; set; }

    [Styled(Category = "Focus")]
    public partial bool ShowFocusRing { get; set; }
    private static bool ShowFocusRingDefault => true;

    // uniform behavior: the focused border changes the same way for everyone,
    // rather than "somewhere it was forgotten". Focus beats hover: the keyboard
    // is working there, and the mouse passing by must not hide that
    protected override Color CurrentBorderColor =>
        ShowsFocusBorder && FocusBorderColor.A > 0 ? FocusBorderColor
        : IsHovered && IsEnabled && HoverBorderColor.A > 0 ? HoverBorderColor
        : BorderColor;

    /// <summary>Whether focus is shown by the border. A field that takes text shows
    /// it always — the user must see where typing goes, however they got there;
    /// anything else — only while focus is visible, like the ring.</summary>
    protected bool ShowsFocusBorder => IsFocused && (AcceptsTextInput || IsFocusVisible);

    protected override bool IsKeyActivatable => true;

    /// <summary>Draw the focus ring if focus is visible. <paramref name="ring"/> is
    /// the line the outer stroke runs along, the inner stroke goes just inside it.
    /// <paramref name="fallback"/> is the color for a control whose theme left
    /// <see cref="FocusRingColor"/> unset — usually its own accent.</summary>
    protected void DrawFocusRing(Graphics g, Rectangle ring, CornerRadius radius, Color fallback = default)
    {
        if (!ShowFocusRing || !IsFocusVisible) return;

        Color outer = FocusRingColor.A > 0 ? FocusRingColor : fallback;
        float thickness = FocusRingThickness;

        if (outer.A == 0 || thickness <= 0f) return;

        g.DrawRoundRectangle(ring, radius, outer, thickness);

        float innerThickness = FocusRingInnerThickness;

        if (innerThickness <= 0f || FocusRingInnerColor.A == 0) return;

        // the inner stroke touches the outer one: their center lines are half
        // of both thicknesses apart
        float step = (thickness + innerThickness) / 2f;
        Rectangle inner = Grow(ring, -step);

        if (inner.Width <= 0f || inner.Height <= 0f) return;

        g.DrawRoundRectangle(inner, Grow(radius, -step), FocusRingInnerColor, innerThickness);
    }

    /// <summary>A rectangle grown on every side; a negative amount shrinks it,
    /// down to nothing but never inside out.</summary>
    protected static Rectangle Grow(Rectangle rect, float amount) => new(
        new Point(rect.X - amount, rect.Y - amount),
        new Size(Math.Max(0f, rect.Width + amount * 2f), Math.Max(0f, rect.Height + amount * 2f)));

    /// <summary>The rounding of a rectangle grown by the same amount: an outline at
    /// a distance keeps the same center of curvature. Never below zero.</summary>
    protected static CornerRadius Grow(CornerRadius radius, float amount) => new(
        Math.Max(0f, radius.TopLeft + amount),
        Math.Max(0f, radius.TopRight + amount),
        Math.Max(0f, radius.BottomRight + amount),
        Math.Max(0f, radius.BottomLeft + amount));
}