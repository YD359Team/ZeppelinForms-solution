using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls;

public class DockPanel : DecoratedPanel
{
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
                    // Fill/None занимают остаток при размещении, но в желаемый
                    // размер панели их вклад входить обязан — иначе панель
                    // окажется высотой только под пришвартованные элементы
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

        // Fill/None откладываем на конец — им достаётся то, что осталось
        // после того, как все "пришвартованные" стороны отъели своё
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

            // "съедаем" использованное пространство из общего прямоугольника
            rect = child.Docking switch
            {
                Dock.Left => new Rectangle(new Point(rect.X + slot.Width + m.Horizontal, rect.Y), new Size(rect.Width - slot.Width - m.Horizontal, rect.Height)),
                Dock.Right => new Rectangle(new Point(rect.X, rect.Y), new Size(rect.Width - slot.Width - m.Horizontal, rect.Height)),
                Dock.Top => new Rectangle(new Point(rect.X, rect.Y + slot.Height + m.Vertical), new Size(rect.Width, rect.Height - slot.Height - m.Vertical)),
                Dock.Bottom => new Rectangle(new Point(rect.X, rect.Y), new Size(rect.Width, rect.Height - slot.Height - m.Vertical)),
                _ => rect,
            };
        }

        foreach (var child in fillers)
        {
            var m = child.Margin;
            child.Arrange(new Rectangle(
                new Point(rect.X + m.Left, rect.Y + m.Top),
                new Size(Math.Max(0, rect.Width - m.Horizontal), Math.Max(0, rect.Height - m.Vertical))));
        }
    }
}