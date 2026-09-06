using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Base;

// Base/InteractiveControl.cs
/// <summary>Оформленный контрол, участвующий в фокусе и вводе.</summary>
public abstract partial class InteractiveControl : DecoratedControl, IInputElement
{
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    [Styled(Category = "Appearance")]
    public partial Color FocusBorderColor { get; set; }
    private static Color FocusBorderColorDefault => Colors.Transparent;

    // единое поведение: рамка в фокусе меняется у всех одинаково,
    // а не «где-то забыли»
    protected override Color CurrentBorderColor =>
        IsFocused && FocusBorderColor.A > 0 ? FocusBorderColor : BorderColor;

    protected override bool IsKeyActivatable => true;
}