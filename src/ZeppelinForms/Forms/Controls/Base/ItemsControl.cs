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
/// A panel whose Children are generated from the Items data collection.
/// </summary>
public class ItemsControl : DecoratedPanel
{
    private float _contentHeight;

    public ObservableCollection<object> Items { get; } = [];

    /// <summary>How to turn a data item into a control. If null, ToString() is used.</summary>
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
                // Reset does not say what exactly was removed — only a full rebuild
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
            // null is not skipped: every item must have its row,
            // see GetOrCreateContainer
            UIElement container = GetOrCreateContainer(items[i]);
            Children.Insert(Math.Clamp(index + i, 0, Children.Count), container);
        }
    }

    private void RemoveContainers(int index, int count, System.Collections.IList? items)
    {
        for (int i = count - 1; i >= 0; i--)
        {
            int position = index + i;
            if (position < 0 || position >= Children.Count) continue;

            // the cache entry leaves only together with its own container:
            // the same object may be in Items twice, and its other row stays
            if (items is not null && i < items.Count && items[i] is { } item &&
                _containers.TryGetValue(item, out UIElement? cached) &&
                ReferenceEquals(cached, Children[position]))
                _containers.Remove(item);

            Children.RemoveAt(position);
        }
    }

    private void MoveContainer(int from, int to)
    {
        if (from < 0 || from >= Children.Count) return;

        UIElement container = Children[from];
        Children.RemoveAt(from);
        Children.Insert(Math.Clamp(to, 0, Children.Count), container);
    }

    /// <summary>A container per data item is created once: when neighbours
    /// are moved or replaced, a row keeps its state.</summary>
    /// <remarks>
    /// A null item still gets a row: containers must stay aligned with Items
    /// by index, otherwise every later Remove or Move by index hits a neighbour.
    /// Such a row is not cached — a dictionary key cannot be null — and does not
    /// go to ItemTemplate, whose signature promises a non-null item.
    ///
    /// The same object may be in Items more than once — interned strings are
    /// the usual case — and one element cannot stand in the panel twice. So the
    /// cached container is reused only while it is not shown here; otherwise
    /// the repeated item gets a fresh container of its own, and only the first
    /// one stays in the cache.
    /// </remarks>
    private UIElement GetOrCreateContainer(object? item)
    {
        if (item is null) return CreateTextContainer(string.Empty);

        if (_containers.TryGetValue(item, out UIElement? existing) &&
            !ReferenceEquals(existing.Parent, this))
            return existing;

        UIElement created = CreateContainer(item);
        _containers.TryAdd(item, created);
        return created;
    }

    protected void RegenerateContainers()
    {
        while (Children.Count > 0)
            Children.RemoveAt(Children.Count - 1);

        _containers.Clear();

        foreach (object? item in Items)
            Children.Add(GetOrCreateContainer(item));

        Invalidate();
    }

    protected virtual UIElement CreateContainer(object item)
    {
        if (ItemTemplate is not null)
            return ItemTemplate(item);

        if (item is UIElement element)
            return element;

        return CreateTextContainer(item?.ToString() ?? string.Empty);
    }

    // the text color is not set here: it comes from the theme, and a hard-coded
    // black made rows invisible on a dark background
    private static Label CreateTextContainer(string text) => new()
    {
        Text = text,
        HorizontalContentAlign = HorizontalContentAlignment.Left,
        VerticalContentAlign = VerticalContentAlignment.Center,
        Padding = new Thickness(6, 3),
    };

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