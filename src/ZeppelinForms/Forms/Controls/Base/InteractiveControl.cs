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

    // uniform behavior: the focused border changes the same way for everyone,
    // rather than "somewhere it was forgotten". Focus beats hover: the keyboard
    // is working there, and the mouse passing by must not hide that
    protected override Color CurrentBorderColor =>
        IsFocused && FocusBorderColor.A > 0 ? FocusBorderColor
        : IsHovered && IsEnabled && HoverBorderColor.A > 0 ? HoverBorderColor
        : BorderColor;

    protected override bool IsKeyActivatable => true;
}