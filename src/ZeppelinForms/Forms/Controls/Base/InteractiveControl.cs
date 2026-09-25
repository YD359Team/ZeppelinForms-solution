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

    // uniform behavior: the focused border changes the same way for everyone,
    // rather than "somewhere it was forgotten"
    protected override Color CurrentBorderColor =>
        IsFocused && FocusBorderColor.A > 0 ? FocusBorderColor : BorderColor;

    protected override bool IsKeyActivatable => true;
}