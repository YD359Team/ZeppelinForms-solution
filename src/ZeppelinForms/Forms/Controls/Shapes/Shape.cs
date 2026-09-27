using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls.Shapes;

public abstract class Shape : UnitControl
{
    // all three change only the picture, so each asks for a redraw:
    // previously a shape kept its old colors until something else redrew it

    public Color Fill
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = Colors.Transparent;

    public Color Stroke
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = Colors.Transparent;

    public float StrokeThickness
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = 1f;

    /// <summary>The shape's default size when Size is not set.</summary>
    protected virtual Size DefaultSize => new(64, 64);

    protected bool HasFill => Fill.A > 0;
    protected bool HasStroke => Stroke.A > 0 && StrokeThickness > 0;

    /// <summary>The stroke is drawn centered on the outline, so half of its
    /// thickness sticks out of the bounds — shrink the area.</summary>
    protected Rectangle StrokeAwareBounds
    {
        get
        {
            if (!HasStroke) return ContentBounds;

            float inset = StrokeThickness / 2f;
            Rectangle bounds = ContentBounds;

            return new Rectangle(
                new Point(bounds.X + inset, bounds.Y + inset),
                new Size(
                    Math.Max(0, bounds.Width - StrokeThickness),
                    Math.Max(0, bounds.Height - StrokeThickness)));
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(
            new Size(
                DefaultSize.Width + Padding.Horizontal,
                DefaultSize.Height + Padding.Vertical),
            availableSize);
}