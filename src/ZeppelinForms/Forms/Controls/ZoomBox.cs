using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Allow zoom in\out for child
/// </summary>
public class ZoomBox : WrapControl, IInputElement
{
    public float ZoomFactor { get; private set; } = 1f;

    // IInputElement
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; }
    public uint TabIndex { get; set; }

    public override void Draw(Graphics g)
    {
    }

    public void Zoom(float factor)
    {
        ZoomFactor = Math.Max(0.01f, factor);
        Invalidate();
    }

    protected internal override void ApplyChildTransform(Graphics g) =>
        g.Scale(ZoomFactor, ZoomFactor);

    protected internal override Point TransformPointToChild(Point point) =>
        new(point.X / ZoomFactor, point.Y / ZoomFactor);

}
