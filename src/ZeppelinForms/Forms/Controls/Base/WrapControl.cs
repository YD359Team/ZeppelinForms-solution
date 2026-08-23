using ZeppelinForms.Drawing.Primitives;

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
            if (field is not null) field.Parent = null;
            field = value;
            if (value is not null) value.Parent = this;
        }
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
}