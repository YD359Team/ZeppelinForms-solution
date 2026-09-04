using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Allow zoom in\out for child
/// </summary>
public class ZoomBox : DecoratedWrapControl, IInputElement
{
    public float ZoomStep { get; set; } = 0.1f;
    public float MinZoom { get; set; } = 0.1f;
    public float MaxZoom { get; set; } = 10f;
    public float ZoomFactor { get; private set; } = 1f;
    // IInputElement
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; }
    public uint TabIndex { get; set; }

    public ZoomBox()
    {
        
    }

    public ZoomBox(UIElement child) : base(child)
    {

    }

    public void Zoom(float factor)
    {
        ZoomFactor = Math.Clamp(factor, MinZoom, MaxZoom);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        float steps = e.Delta / 120f;
        Zoom(ZoomFactor * MathF.Pow(1f + ZoomStep, steps));
        e.Handled = true;
    }

    protected internal override void ApplyChildTransform(Graphics g) =>
        g.Scale(ZoomFactor, ZoomFactor);

    protected internal override Point TransformPointToChild(Point point) =>
        new(point.X / ZoomFactor, point.Y / ZoomFactor);
}
