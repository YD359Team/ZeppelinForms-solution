using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ZeppelinForms.Dispatchers;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Data;

/// <summary>The items of a list control: a collection of its own, or, with a
/// <c>DataSource</c>, a mirror of the source that only the source changes.</summary>
/// <remarks>
/// The property stays an <see cref="ObservableCollection{T}"/>: code reading the items,
/// listening to them, adding to them without a data source goes on as it did. Bound,
/// a change from outside throws instead of quietly going out of step with the source,
/// which would undo it on its next change.
/// </remarks>
internal sealed class ItemCollection : ObservableCollection<object>, IItemsSink
{
    private bool _writing;

    /// <summary>The items mirror a data source.</summary>
    public bool IsBound { get; set; }

    protected override void InsertItem(int index, object item)
    {
        Guard();
        base.InsertItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        Guard();
        base.RemoveItem(index);
    }

    protected override void SetItem(int index, object item)
    {
        Guard();
        base.SetItem(index, item);
    }

    protected override void MoveItem(int oldIndex, int newIndex)
    {
        Guard();
        base.MoveItem(oldIndex, newIndex);
    }

    protected override void ClearItems()
    {
        Guard();
        base.ClearItems();
    }

    private void Guard()
    {
        if (IsBound && !_writing)
        {
            throw new InvalidOperationException(
                "The items mirror the DataSource: change the source, or set DataSource to null first.");
        }
    }

    IReadOnlyList<object> IItemsSink.Current => this;

    void IItemsSink.Insert(int index, object item) => Write(() => InsertItem(index, item));

    void IItemsSink.RemoveAt(int index) => Write(() => RemoveItem(index));

    void IItemsSink.Replace(int index, object item) => Write(() => SetItem(index, item));

    void IItemsSink.Move(int from, int to) => Write(() => MoveItem(from, to));

    /// <summary>Every item at once, with one Reset: a thousand records added one by one
    /// were a thousand changes for a sorted grid to sort again.</summary>
    void IItemsSink.ResetTo(IReadOnlyList<object> items)
    {
        Items.Clear();

        foreach (object item in items)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void Write(Action action)
    {
        _writing = true;

        try
        {
            action();
        }
        finally
        {
            _writing = false;
        }
    }
}

/// <summary>Where a mirror writes: the items of a control.</summary>
internal interface IItemsSink
{
    IReadOnlyList<object> Current { get; }

    void Insert(int index, object item);

    void RemoveAt(int index);

    void Replace(int index, object item);

    void Move(int from, int to);

    void ResetTo(IReadOnlyList<object> items);
}

/// <summary>Keeps the items of a control in step with its data source.</summary>
/// <remarks>
/// <para>
/// A source that tells about its changes is followed change by change, so a list
/// keeps its selection and its rows; one that doesn't is read when set and on
/// <see cref="Resync"/>. A change that can't be followed — a Reset, a change from
/// another thread — makes the items be compared with the source anew.
/// </para>
/// <para>
/// The source is listened to only while the control is in a form: the source's events
/// hold the control, and a long-lived model would hold every window that ever showed
/// it. Coming back into a form, the control reads the source again.
/// </para>
/// <para>
/// A change from another thread is not applied there: the items belong to the UI
/// thread. It is queued to the control's dispatcher, once for a burst of changes, and
/// the items are compared with the source there.
/// </para>
/// </remarks>
internal sealed class DataSourceMirror(UIElement owner, IItemsSink sink, Action? changed = null)
{
    /// <summary>Above this many removals and insertions a resync is one Reset rather
    /// than a change per item.</summary>
    private const int MaxStepwiseChanges = 32;

    private bool _listening;
    private int _resyncQueued;

    public IEnumerable? Source { get; private set; }

    public void SetSource(IEnumerable? source)
    {
        if (ReferenceEquals(source, Source)) return;

        StopListening();

        Source = source;

        if (source is null) return;

        Resync();

        if (owner.FindForm() is not null) Listen();
    }

    /// <summary>The control came into a form: listen, and catch up on what changed
    /// while it wasn't listening.</summary>
    public void OnAttached()
    {
        if (Source is null || _listening) return;

        Resync();
        Listen();
    }

    public void OnDetached() => StopListening();

    /// <summary>Make the items the source's, by the fewest changes when they are few.</summary>
    public void Resync()
    {
        if (Source is null) return;

        List<object> target = [];

        foreach (object? item in Source)
            if (item is not null)
                target.Add(item);

        IReadOnlyList<object> current = sink.Current;

        if (SameSequence(current, target)) return;

        if (!TryStepwise(current, target)) sink.ResetTo(target);

        changed?.Invoke();
    }

    private void Listen()
    {
        switch (Source)
        {
            case INotifyCollectionChanged observable:
                observable.CollectionChanged += OnSourceChanged;
                break;

            case IBindingList binding:
                binding.ListChanged += OnSourceListChanged;
                break;
        }

        _listening = true;
    }

    private void StopListening()
    {
        if (!_listening) return;

        switch (Source)
        {
            case INotifyCollectionChanged observable:
                observable.CollectionChanged -= OnSourceChanged;
                break;

            case IBindingList binding:
                binding.ListChanged -= OnSourceListChanged;
                break;
        }

        _listening = false;
    }

    private Dispatcher Dispatcher => owner.FindForm()?.Dispatcher ?? Dispatcher.UIThread;

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            QueueResync();
            return;
        }

        if (!TryApply(e)) Resync();
        else changed?.Invoke();
    }

    private void OnSourceListChanged(object? sender, ListChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            QueueResync();
            return;
        }

        IList list = (IList)sender!;

        bool applied = e.ListChangedType switch
        {
            ListChangedType.ItemAdded when InRange(e.NewIndex, sink.Current.Count + 1) && list[e.NewIndex] is { } added
                => Do(() => sink.Insert(e.NewIndex, added)),
            ListChangedType.ItemDeleted when InRange(e.NewIndex, sink.Current.Count)
                => Do(() => sink.RemoveAt(e.NewIndex)),
            ListChangedType.ItemMoved when InRange(e.OldIndex, sink.Current.Count) && InRange(e.NewIndex, sink.Current.Count)
                => Do(() => sink.Move(e.OldIndex, e.NewIndex)),

            // the record is the same object: the control redraws it by itself
            ListChangedType.ItemChanged => true,
            _ => false,
        };

        if (!applied) Resync();
        else changed?.Invoke();
    }

    /// <summary>One change of the source, applied as it is. False — it can't be: its
    /// indices aren't given, or don't fit the items, which then are compared anew.</summary>
    private bool TryApply(NotifyCollectionChangedEventArgs e)
    {
        int count = sink.Current.Count;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is { } added && InRange(e.NewStartingIndex, count + 1):
                for (int i = 0; i < added.Count; i++)
                {
                    if (added[i] is not { } item) return false;

                    sink.Insert(e.NewStartingIndex + i, item);
                }

                return true;

            case NotifyCollectionChangedAction.Remove when e.OldItems is { } removed &&
                                                           e.OldStartingIndex >= 0 && e.OldStartingIndex + removed.Count <= count:
                for (int i = 0; i < removed.Count; i++)
                    sink.RemoveAt(e.OldStartingIndex);

                return true;

            case NotifyCollectionChangedAction.Replace when e.NewItems is { Count: 1 } replaced &&
                                                            InRange(e.NewStartingIndex, count) && replaced[0] is { } item:
                sink.Replace(e.NewStartingIndex, item);
                return true;

            case NotifyCollectionChangedAction.Move when e.OldItems is { Count: 1 } &&
                                                         InRange(e.OldStartingIndex, count) && InRange(e.NewStartingIndex, count):
                sink.Move(e.OldStartingIndex, e.NewStartingIndex);
                return true;

            default:
                return false;
        }
    }

    private void QueueResync()
    {
        if (Interlocked.Exchange(ref _resyncQueued, 1) == 1) return;

        Dispatcher.BeginInvoke(() =>
        {
            Volatile.Write(ref _resyncQueued, 0);
            Resync();
        });
    }

    /// <summary>The items made the target's by removals and insertions, when the items
    /// kept stay in their order and the changes are few — a filter narrowed or widened.
    /// False — a Reset is the cheaper way.</summary>
    private bool TryStepwise(IReadOnlyList<object> current, List<object> target)
    {
        var wanted = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);

        foreach (object item in target)
            wanted[item] = wanted.GetValueOrDefault(item) + 1;

        // what goes: the items the target hasn't, or has fewer of
        var keep = new Dictionary<object, int>(wanted, ReferenceEqualityComparer.Instance);
        List<int> removals = [];

        for (int i = 0; i < current.Count; i++)
        {
            if (keep.TryGetValue(current[i], out int left) && left > 0) keep[current[i]] = left - 1;
            else removals.Add(i);
        }

        int insertions = target.Count - (current.Count - removals.Count);

        if (removals.Count + insertions > MaxStepwiseChanges) return false;

        // what stays must stay in its order: a reordering is a Reset
        List<object> kept = [.. current.Where((_, i) => !removals.Contains(i))];
        var stillWanted = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);

        foreach (object item in kept)
            stillWanted[item] = stillWanted.GetValueOrDefault(item) + 1;

        List<object> targetKept = [];

        foreach (object item in target)
        {
            if (!stillWanted.TryGetValue(item, out int n) || n == 0) continue;

            stillWanted[item] = n - 1;
            targetKept.Add(item);
        }

        if (!SameSequence(kept, targetKept)) return false;

        for (int i = removals.Count - 1; i >= 0; i--)
            sink.RemoveAt(removals[i]);

        // the insertions in the target's order: walking it, an item where the items
        // differ is the one to insert
        for (int i = 0; i < target.Count; i++)
        {
            if (i < sink.Current.Count && ReferenceEquals(sink.Current[i], target[i])) continue;

            sink.Insert(i, target[i]);
        }

        return true;
    }

    private static bool SameSequence(IReadOnlyList<object> left, List<object> right)
    {
        if (left.Count != right.Count) return false;

        for (int i = 0; i < left.Count; i++)
            if (!ReferenceEquals(left[i], right[i]))
                return false;

        return true;
    }

    private static bool InRange(int index, int count) => index >= 0 && index < count;

    private static bool Do(Action action)
    {
        action();
        return true;
    }
}
