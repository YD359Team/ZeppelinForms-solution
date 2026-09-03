using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Simple panel
/// </summary>
public class Panel : DecoratedPanel
{
    protected override Size MeasureContentOverride(Size availableSize)
    {
        // canvas-style: дети сами решают, какого они размера,
        // панель их не ужимает и под них не подстраивается
        foreach (UIElement child in Children)
        {
            if (!child.IsVisible) continue;

            child.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
        }

        return ResolveSize(Size.Empty, availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        foreach (UIElement child in Children)
        {
            if (!child.IsVisible) continue;

            child.Arrange(new Rectangle(child.Position, child.DesiredSize));
        }
    }
}
