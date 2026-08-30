using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Панель, чьи Children генерируются из коллекции данных Items.
/// </summary>
public class ItemsControl : PanelControl
{
    private float _contentHeight;

    public ObservableCollection<object> Items { get; } = [];

    /// <summary>Как превратить элемент данных в контрол. Если null — используется ToString().</summary>
    public Func<object, UIElement>? ItemTemplate { get; set; }

    public ItemsControl()
    {
        Items.CollectionChanged += Items_CollectionChanged;
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RegenerateContainers();

    protected virtual UIElement CreateContainer(object item)
    {
        if (ItemTemplate is not null)
            return ItemTemplate(item);

        if (item is UIElement element)
            return element;

        return new Label
        {
            Text = item?.ToString() ?? string.Empty,
            TextColor = Colors.Black,
            HorizontalContentAlign = HorizontalContentAlignment.Left,
            VerticalContentAlign = VerticalContentAlignment.Center,
            Padding = new Thickness(6, 3),
        };
    }

    protected void RegenerateContainers()
    {
        // ВАЖНО: Children.Clear() поднимает Reset, у которого OldItems == null,
        // поэтому PanelControl не смог бы отвязать Parent у старых детей.
        // Удаляем поштучно — каждый Remove несёт нормальный OldItems.
        while (Children.Count > 0)
            Children.RemoveAt(Children.Count - 1);

        foreach (var item in Items)
            Children.Add(CreateContainer(item));

        Invalidate();
    }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        float totalHeight = 0;
        float maxWidth = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;

            child.Measure(new Size(inner.Width, float.PositiveInfinity));
            totalHeight += child.DesiredSize.Height;
            maxWidth = Math.Max(maxWidth, child.DesiredSize.Width);
        }

        _contentHeight = totalHeight;

        var content = new Size(maxWidth + Padding.Horizontal, totalHeight + Padding.Vertical);
        return ResolveSize(content, availableSize);
    }

    protected override void ArrangeContentOverride(Size finalSize)
    {
        var content = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical)));

        float y = content.Y;

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;

            child.Arrange(new Rectangle(
                new Point(content.X, y),
                new Size(content.Width, child.DesiredSize.Height)));

            y += child.ActualSize.Height;
        }
    }


    public override void Draw(Graphics g)
    {
        //
    }
}
