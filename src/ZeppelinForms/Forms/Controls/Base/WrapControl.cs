using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Control with single child (or nothing)
/// </summary>
public abstract class WrapControl : UIElement
{
    public UIElement? Child
    {
        get;
        set
        {
            if (field == value) return;

            Form? owner = FindOwner();

            if (field is not null)
            {
                owner?.DetachTree(field);
                field.Parent = null;
            }

            field = value;

            if (value is not null)
            {
                value.Parent = this;
                owner?.AttachTree(value);
            }

            // the content changed — the element's size is computed from it.
            // This applies to removing the child as well: without it a container
            // whose child was set to null kept the size of the departed content
            Invalidate();
        }
    }

    public WrapControl()
    {

    }

    public WrapControl(UIElement child)
    {
        this.Child = child;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var content = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        Size childDesired = Size.Empty;
        if (Child is not null)
        {
            Child.Measure(content);
            childDesired = Child.DesiredSize;
        }

        var total = new Size(
            childDesired.Width + Padding.Horizontal,
            childDesired.Height + Padding.Vertical);

        return ResolveSize(total, availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not null)
        {
            var rect = new Rectangle(
                new Point(Padding.Left, Padding.Top),
                new Size(
                    Math.Max(0, finalSize.Width - Padding.Horizontal),
                    Math.Max(0, finalSize.Height - Padding.Vertical)));

            Child.Arrange(rect);
        }

        return finalSize;
    }

    // A hook for derived classes like ZoomBox — apply their own transform
    // (scale, rotation, etc.) to the canvas right before the child is drawn.
    // Does nothing by default.
    protected internal virtual void ApplyChildTransform(Graphics g) { }

    /// <summary>The content is drawn with a transform rather than a plain offset.
    /// The tree walk then cannot cull the subtree by rectangles: in absolute
    /// coordinates the child would not be where adding up offsets expects it.</summary>
    protected internal virtual bool TransformsChild => false;

    // The mirror of ApplyChildTransform for hit testing: if the child is drawn
    // transformed, the mouse coordinates must be transformed the same way
    // (with the inverse transform) before hit testing the child.
    protected internal virtual Point TransformPointToChild(Point point) => point;
}