using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Docks its children to its edges one after another, each to what the ones before
/// it left; the children docked to no edge share the rest.
/// </summary>
public partial class DockPanel : DecoratedPanel
{
    /// <summary>The last child takes the rest of the room as <see cref="Dock.Fill"/>
    /// would, when it is docked to no edge of its own. True by default.</summary>
    /// <remarks>
    /// <para>
    /// Without it the content added last after a tool bar and a status bar got the
    /// rest of the room as its slot, but kept its own alignment in it: a data grid
    /// filled it, a text box stood in its middle, and only <c>Dock.Fill</c> written
    /// by hand made it fill.
    /// </para>
    /// <para>
    /// Unlike WPF's, a side set explicitly is kept: there the last child fills
    /// whatever its <c>DockPanel.Dock</c> says, and a status bar added after the
    /// content becomes the content. False — the last child keeps its alignment.
    /// </para>
    /// </remarks>
    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial bool LastChildFill { get; set; }
    private static bool LastChildFillDefault => true;

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var remaining = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        float usedWidth = 0, usedHeight = 0;
        float maxRowWidth = 0, maxColHeight = 0;
        float fillWidth = 0, fillHeight = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            var m = child.Margin;

            var childAvailable = new Size(
                Math.Max(0, remaining.Width - usedWidth - m.Horizontal),
                Math.Max(0, remaining.Height - usedHeight - m.Vertical));

            child.Measure(childAvailable);

            switch (child.Docking)
            {
                case Dock.Left or Dock.Right:
                    usedWidth += child.DesiredSize.Width + m.Horizontal;
                    maxColHeight = Math.Max(maxColHeight, child.DesiredSize.Height + m.Vertical);
                    break;

                case Dock.Top or Dock.Bottom:
                    usedHeight += child.DesiredSize.Height + m.Vertical;
                    maxRowWidth = Math.Max(maxRowWidth, child.DesiredSize.Width + m.Horizontal);
                    break;

                default:
                    // Fill/None take the remainder when arranged, but their
                    // contribution must be part of the panel's desired size —
                    // otherwise the panel ends up tall enough only for the docked elements
                    fillWidth = Math.Max(fillWidth, child.DesiredSize.Width + m.Horizontal);
                    fillHeight = Math.Max(fillHeight, child.DesiredSize.Height + m.Vertical);
                    break;
            }
        }

        var content = new Size(
            Math.Max(Math.Max(usedWidth, maxRowWidth), usedWidth + fillWidth),
            Math.Max(Math.Max(usedHeight, maxColHeight), usedHeight + fillHeight));

        content = new Size(content.Width + Padding.Horizontal, content.Height + Padding.Vertical);
        return ResolveSize(content, availableSize);
    }

    protected override void ArrangeContentOverride(Size finalSize)
    {
        var rect = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical)));

        // Fill/None are deferred to the end — they get what is left
        // after all the "docked" sides have taken their share
        var docked = Children.Where(c => c.IsVisible && c.Docking is not (Dock.None or Dock.Fill));
        var fillers = Children.Where(c => c.IsVisible && c.Docking is Dock.None or Dock.Fill);

        foreach (var child in docked)
        {
            var m = child.Margin;

            Rectangle slot = child.Docking switch
            {
                Dock.Left => new Rectangle(
                    new Point(rect.X + m.Left, rect.Y + m.Top),
                    new Size(child.DesiredSize.Width, Math.Max(0, rect.Height - m.Vertical))),

                Dock.Right => new Rectangle(
                    new Point(rect.X + rect.Width - child.DesiredSize.Width - m.Right, rect.Y + m.Top),
                    new Size(child.DesiredSize.Width, Math.Max(0, rect.Height - m.Vertical))),

                Dock.Top => new Rectangle(
                    new Point(rect.X + m.Left, rect.Y + m.Top),
                    new Size(Math.Max(0, rect.Width - m.Horizontal), child.DesiredSize.Height)),

                Dock.Bottom => new Rectangle(
                    new Point(rect.X + m.Left, rect.Y + rect.Height - child.DesiredSize.Height - m.Bottom),
                    new Size(Math.Max(0, rect.Width - m.Horizontal), child.DesiredSize.Height)),

                _ => rect,
            };

            child.Arrange(slot);

            // "eat" the used space out of the common rectangle
            rect = child.Docking switch
            {
                Dock.Left => new Rectangle(new Point(rect.X + slot.Width + m.Horizontal, rect.Y), new Size(rect.Width - slot.Width - m.Horizontal, rect.Height)),
                Dock.Right => new Rectangle(new Point(rect.X, rect.Y), new Size(rect.Width - slot.Width - m.Horizontal, rect.Height)),
                Dock.Top => new Rectangle(new Point(rect.X, rect.Y + slot.Height + m.Vertical), new Size(rect.Width, rect.Height - slot.Height - m.Vertical)),
                Dock.Bottom => new Rectangle(new Point(rect.X, rect.Y), new Size(rect.Width, rect.Height - slot.Height - m.Vertical)),
                _ => rect,
            };
        }

        // the last child fills the rest even with no Dock.Fill of its own
        UIElement? last = LastChildFill ? Children.LastOrDefault(c => c.IsVisible) : null;

        foreach (var child in fillers)
        {
            var m = child.Margin;
            child.Arrange(
                new Rectangle(
                    new Point(rect.X + m.Left, rect.Y + m.Top),
                    new Size(Math.Max(0, rect.Width - m.Horizontal), Math.Max(0, rect.Height - m.Vertical))),
                fill: child.Docking == Dock.Fill || ReferenceEquals(child, last));
        }
    }
}