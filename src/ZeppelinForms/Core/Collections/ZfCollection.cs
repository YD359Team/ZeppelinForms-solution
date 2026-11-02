using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ZeppelinForms.Core.Collections;

/// <summary>
/// The collections of the controls: an <see cref="ObservableCollection{T}"/>, so that
/// adding a series to a chart or an item to a menu shows at once and a binding to
/// <c>Count</c> follows, with the batch operations a <see cref="List{T}"/> had — each of
/// them one notification, not one per item.
/// </summary>
/// <remarks>
/// Until 0.20 these properties were <see cref="List{T}"/>: a change after the control was
/// shown went unseen until something else asked for a redraw. Collection expressions and
/// initializers work as they did — <c>Values = [1, 2, 3]</c>, <c>Items = { a, b }</c> —
/// and so does <see cref="AddRange"/>.
/// </remarks>
public class ZfCollection<T> : ObservableCollection<T>
{
    public ZfCollection()
    {
    }

    public ZfCollection(IEnumerable<T> items) : base(items)
    {
    }

    /// <summary>Add the items at the end with one notification: an Add of all of them.</summary>
    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        CheckReentrancy();

        int start = Count;
        List<T> added = [.. items];

        if (added.Count == 0) return;

        foreach (T item in added)
            Items.Add(item);

        RaiseChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, added, start));
    }

    /// <summary>Put these items in place of all there are, with one notification: a Reset.</summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        CheckReentrancy();

        // read before clearing: the items may come from this very collection
        List<T> next = [.. items];

        if (Count == 0 && next.Count == 0) return;

        Items.Clear();

        foreach (T item in next)
            Items.Add(item);

        RaiseReset();
    }

    /// <summary>Remove every item the predicate takes, with one notification — a Reset —
    /// when any was removed.</summary>
    /// <returns>How many were removed.</returns>
    public int RemoveAll(Predicate<T> match)
    {
        ArgumentNullException.ThrowIfNull(match);

        CheckReentrancy();

        int removed = 0;

        for (int i = Count - 1; i >= 0; i--)
        {
            if (!match(Items[i])) continue;

            Items.RemoveAt(i);
            removed++;
        }

        if (removed > 0) RaiseReset();

        return removed;
    }

    /// <summary>Sort in place, with one notification: a Reset. Stable: items the comparison
    /// calls equal keep their order, unlike <see cref="List{T}.Sort(Comparison{T})"/>.</summary>
    public void Sort(Comparison<T> comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        CheckReentrancy();

        if (Count < 2) return;

        T[] sorted = [.. Items.Select((item, index) => (item, index))
            .OrderBy(pair => pair.item, Comparer<T>.Create(comparison))
            .ThenBy(pair => pair.index)
            .Select(pair => pair.item)];

        for (int i = 0; i < sorted.Length; i++)
            Items[i] = sorted[i];

        RaiseReset();
    }

    /// <summary>Sort in place by the default order of the items.</summary>
    public void Sort() => Sort(Comparer<T>.Default.Compare);

    /// <summary>Sort in place by a comparer.</summary>
    public void Sort(IComparer<T> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);

        Sort(comparer.Compare);
    }

    private void RaiseReset() => RaiseChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

    private void RaiseChanged(NotifyCollectionChangedEventArgs args)
    {
        OnPropertyChanged(s_count);
        OnPropertyChanged(s_indexer);
        OnCollectionChanged(args);
    }

    // the names ObservableCollection itself raises
    private static readonly PropertyChangedEventArgs s_count = new(nameof(Count));
    private static readonly PropertyChangedEventArgs s_indexer = new("Item[]");
}

/// <summary>How a control keeps to its collections: the handler on the one it has, and on
/// the one an initializer gives it instead.</summary>
internal static class ZfCollectionOwner
{
    private static readonly NotifyCollectionChangedEventArgs s_reset = new(NotifyCollectionChangedAction.Reset);

    /// <summary>Take a collection given in place of the control's own — <c>Values = [1, 2]</c>
    /// in an initializer —: the handler leaves the old one for it, and hears of it as a reset.</summary>
    public static void Replace<T>(ref ZfCollection<T> field, ZfCollection<T> value, NotifyCollectionChangedEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (ReferenceEquals(field, value)) return;

        field.CollectionChanged -= handler;
        field = value;
        value.CollectionChanged += handler;

        handler(value, s_reset);
    }
}
