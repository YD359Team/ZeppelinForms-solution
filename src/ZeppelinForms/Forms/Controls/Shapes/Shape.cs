using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls.Shapes;

public abstract class Shape : UnitControl
{
    public Color Fill { get; set; } = Colors.Transparent;
    public Color Stroke { get; set; } = Colors.Transparent;
    public float StrokeThickness { get; set; } = 1f;

    /// <summary>Размер фигуры по умолчанию, когда Size не задан.</summary>
    protected virtual Size DefaultSize => new(64, 64);

    protected bool HasFill => Fill.A > 0;
    protected bool HasStroke => Stroke.A > 0 && StrokeThickness > 0;

    /// <summary>Обводка рисуется по центру контура, поэтому половина
    /// её толщины вылезает за границы — сжимаем область.</summary>
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
