using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Панель, чьи Children генерируются из коллекции данных Items.
/// </summary>
public class ItemsControl : DecoratedPanel
{
    private float _contentHeight;

    public ObservableCollection<object> Items { get; } = [];

    /// <summary>Как превратить элемент данных в контрол. Если null — используется ToString().</summary>
    public Func<object, UIElement>? ItemTemplate { get; set; }

    public ItemsControl()
    {
        Items.CollectionChanged += Items_CollectionChanged;
    }

    private readonly Dictionary<object, UIElement> _containers = new(ReferenceEqualityComparer.Instance);

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                InsertContainers(e.NewStartingIndex, e.NewItems);
                break;

            case NotifyCollectionChangedAction.Remove:
                RemoveContainers(e.OldStartingIndex, e.OldItems?.Count ?? 0, e.OldItems);
                break;

            case NotifyCollectionChangedAction.Replace:
                RemoveContainers(e.OldStartingIndex, e.OldItems?.Count ?? 0, e.OldItems);
                InsertContainers(e.NewStartingIndex, e.NewItems);
                break;

            case NotifyCollectionChangedAction.Move:
                MoveContainer(e.OldStartingIndex, e.NewStartingIndex);
                break;

            default:
                // Reset не сообщает, что именно убрали — только полная пересборка
                RegenerateContainers();
                return;
        }

        Invalidate();
    }

    private void InsertContainers(int index, System.Collections.IList? items)
    {
        if (items is null) return;

        for (int i = 0; i < items.Count; i++)
        {
            object? item = items[i];
            if (item is null) continue;

            UIElement container = GetOrCreateContainer(item);
            Children.Insert(Math.Clamp(index + i, 0, Children.Count), container);
        }
    }

    private void RemoveContainers(int index, int count, System.Collections.IList? items)
    {
        for (int i = count - 1; i >= 0; i--)
        {
            int position = index + i;
            if (position < 0 || position >= Children.Count) continue;

            Children.RemoveAt(position);
        }

        if (items is null) return;

        foreach (object? item in items)
            if (item is not null)
                _containers.Remove(item);
    }

    private void MoveContainer(int from, int to)
    {
        if (from < 0 || from >= Children.Count) return;

        UIElement container = Children[from];
        Children.RemoveAt(from);
        Children.Insert(Math.Clamp(to, 0, Children.Count), container);
    }

    /// <summary>Контейнер на элемент данных создаётся один раз: при переносе
    /// или замене соседей строка сохраняет своё состояние.</summary>
    private UIElement GetOrCreateContainer(object item)
    {
        if (_containers.TryGetValue(item, out UIElement? existing))
            return existing;

        UIElement created = CreateContainer(item);
        _containers[item] = created;
        return created;
    }

    protected void RegenerateContainers()
    {
        while (Children.Count > 0)
            Children.RemoveAt(Children.Count - 1);

        _containers.Clear();

        foreach (object item in Items)
            Children.Add(GetOrCreateContainer(item));

        Invalidate();
    }

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
}
