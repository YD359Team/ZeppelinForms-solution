using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ZeppelinForms.Data;

/// <summary>A typed view of a collection for a list control: its records, filtered and
/// ordered, following the collection as it changes.</summary>
/// <typeparam name="T">The type of the records.</typeparam>
/// <remarks>
/// <para>
/// Given as the <c>DataSource</c> of a <see cref="Forms.Controls.DataGrid.DataGridView"/>,
/// a list box or a combo box, it is what the control shows: the records of the
/// collection that pass <see cref="Filter"/>, in the order of <see cref="Comparer"/>.
/// The collection itself is not changed, and several views of one collection — a grid
/// of everything and a list of what is due today — each keep their own.
/// </para>
/// <para>
/// A collection that tells about its changes — <see cref="INotifyCollectionChanged"/>
/// (an <c>ObservableCollection&lt;T&gt;</c>) or <see cref="IBindingList"/>
/// (a <c>BindingList&lt;T&gt;</c>) — is followed: an added record shows, a removed one
/// goes. Any other collection — a list, an array, a query — is read when the view is
/// made and on <see cref="Refresh"/>. So is a record whose change makes it pass the
/// filter or move in the order: the view learns of it on Refresh.
/// </para>
/// <para>
/// Changes are told on the thread they happen on; a control showing the view takes
/// them on its UI thread. Change the collection on one thread at a time.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var due = new DataSource&lt;Chore&gt;(chores) { Filter = chore => !chore.Done };
/// grid.DataSource = due;
/// search.TextChanged += (_, _) => due.Filter = chore => chore.Title.Contains(search.Text);
/// </code>
/// </example>
public sealed class DataSource<T> : IReadOnlyList<T>, IList, INotifyCollectionChanged, IDisposable
{
    private List<T> _view = [];

    /// <param name="source">The collection the view shows.</param>
    public DataSource(IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Source = source;

        switch (source)
        {
            case INotifyCollectionChanged observable:
                observable.CollectionChanged += OnSourceChanged;
                break;

            case IBindingList binding:
                binding.ListChanged += OnSourceListChanged;
                break;
        }

        Rebuild();
    }

    /// <summary>The collection the view shows.</summary>
    public IEnumerable<T> Source { get; }

    /// <summary>Which records the view shows; null — every one.</summary>
    /// <remarks>Set, the view is built again: a search box sets a new filter on every
    /// keystroke.</remarks>
    public Func<T, bool>? Filter
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Refresh();
        }
    }

    /// <summary>The order of the records; null — the collection's own.</summary>
    /// <remarks>The order the view hands to the control. A grid sorted by a header
    /// click orders its rows on top of it, as a way of looking, and leaves this one be.</remarks>
    public IComparer<T>? Comparer
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Refresh();
        }
    }

    /// <summary>The view changed: records came, went or moved.</summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public int Count => _view.Count;

    public T this[int index] => _view[index];

    /// <summary>Read the collection again, filter and order it: after a collection that
    /// doesn't tell about its changes changed, or a record changed so that it passes
    /// the filter now, or stands elsewhere in the order.</summary>
    public void Refresh()
    {
        Rebuild();
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>Stop following the collection: it holds the view through its events,
    /// and a view made for a window closed long ago would go on being rebuilt.</summary>
    public void Dispose()
    {
        switch (Source)
        {
            case INotifyCollectionChanged observable:
                observable.CollectionChanged -= OnSourceChanged;
                break;

            case IBindingList binding:
                binding.ListChanged -= OnSourceListChanged;
                break;
        }
    }

    public IEnumerator<T> GetEnumerator() => _view.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Rebuild()
    {
        // a copy: the collection may be a query, read once; or a list changed while it
        // is walked, which would throw in the middle of a frame
        IEnumerable<T> records = Source;

        if (Filter is { } filter) records = records.Where(filter);
        if (Comparer is { } comparer) records = records.Order(comparer);

        _view = [.. records];
    }

    /// <summary>The collection changed. A plain view passes the change on as it is —
    /// the control keeps its selection and its rows; a filtered or ordered one is
    /// built again.</summary>
    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Filter is null && Comparer is null && IsPassable(e))
        {
            Rebuild();
            CollectionChanged?.Invoke(this, e);
            return;
        }

        Refresh();
    }

    private void OnSourceListChanged(object? sender, ListChangedEventArgs e)
    {
        // a record's property changed: its row redraws by itself through the record,
        // and only a filter or an order would need the view built again
        if (e.ListChangedType == ListChangedType.ItemChanged && Filter is null && Comparer is null) return;

        Refresh();
    }

    /// <summary>A change whose indices are the view's too: one with the indices given.</summary>
    private static bool IsPassable(NotifyCollectionChangedEventArgs e) => e.Action switch
    {
        NotifyCollectionChangedAction.Add => e.NewStartingIndex >= 0,
        NotifyCollectionChangedAction.Remove => e.OldStartingIndex >= 0,
        NotifyCollectionChangedAction.Replace => e.NewStartingIndex >= 0,
        NotifyCollectionChangedAction.Move => e.OldStartingIndex >= 0 && e.NewStartingIndex >= 0,
        _ => false,
    };

    // ===== IList: a view is read-only; the list interface is what a control reads =====

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => true;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    object? IList.this[int index]
    {
        get => _view[index];
        set => throw ReadOnly();
    }

    int IList.Add(object? value) => throw ReadOnly();

    void IList.Clear() => throw ReadOnly();

    bool IList.Contains(object? value) => value is T record && _view.Contains(record);

    int IList.IndexOf(object? value) => value is T record ? _view.IndexOf(record) : -1;

    void IList.Insert(int index, object? value) => throw ReadOnly();

    void IList.Remove(object? value) => throw ReadOnly();

    void IList.RemoveAt(int index) => throw ReadOnly();

    void ICollection.CopyTo(Array array, int index) => ((ICollection)_view).CopyTo(array, index);

    private static NotSupportedException ReadOnly() =>
        new("A DataSource is a view of its collection: change the collection, and the view follows.");
}
