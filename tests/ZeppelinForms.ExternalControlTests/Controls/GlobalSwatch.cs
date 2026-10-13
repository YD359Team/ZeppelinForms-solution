using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;

// No namespace on purpose: a control of a small app's top-level program lives in the
// global namespace, and the styled property generator used to fail on it — for every
// control of the assembly, not just this one

/// <summary>A control in the global namespace, with a styled property.</summary>
public partial class GlobalSwatch : DecoratedControl
{
    [Styled(Category = "Appearance")]
    public partial Color Fill { get; set; }

    protected override void DrawContent(Graphics g) => g.FillRectangle(LocalBounds, Fill);
}