using System.Collections.Specialized;
using System.ComponentModel;
using ZeppelinForms.Dispatchers;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>The grid follows its records: a value that changes in a record shows in
/// its row without the mouse having to pass over the grid.</summary>
/// <remarks>
/// <para>
/// The grid drew its rows when something else asked for a frame — a hover, a click,
/// a scroll. A record updated by a timer kept its old values on screen until the
/// pointer moved over the grid: the memory column of a process list stood still.
/// </para>
/// <para>
/// Records implementing <see cref="INotifyPropertyChanged"/> are listened to as they
/// come into <see cref="Items"/> and left alone as they go. Records that don't tell
/// about their changes — plain classes filled by a timer — need
/// <see cref="Refresh"/> once their update is done. Either may come from any thread:
/// the redraw is queued to the UI thread, once for a whole burst of changes.
/// </para>
/// </remarks>
public partial class DataGridView
{
    /// <summary>The records listened to, with how many times each is in Items: the same
    /// record twice is listened to once, and stays listened to until both are gone.</summary>
    private readonly Dictionary<INotifyPropertyChanged, int> _observed = new(ReferenceEqualityComparer.Instance);

    /// <summary>A redraw is queued and not yet run: the changes after it ride along.</summary>
    private int _refreshQueued;

    /// <summary>The form the grid is in, for changes from other threads: its dispatcher
    /// is asked at the time of the change — a form gets the one of its window only once
    /// the window exists, after its content was attached. Null — not in a form.</summary>
    private volatile Form? _form;

    /// <summary>The records changed: read the values again and draw the rows, sorted
    /// again if the grid is sorted. Safe to call from any thread.</summary>
    /// <remarks>For records that don't implement <see cref="INotifyPropertyChanged"/>:
    /// call it once after updating them, not once per record — the calls of a burst
    /// make one redraw anyway.</remarks>
    public void Refresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;

        (_form?.Dispatcher ?? Dispatcher.UIThread).BeginInvoke(ApplyRefresh);
    }

    private void ApplyRefresh()
    {
        Volatile.Write(ref _refreshQueued, 0);

        // the order was built from the old values: a sorted grid sorts again, the
        // selection staying on its record
        if (SortColumnIndex >= 0)
        {
            BuildOrder();
            RestoreSelection(null);
        }

        // Auto widths are measured from the values on every layout
        Invalidate();
    }

    private void OnRecordChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    /// <summary>Listen to the records that came and stop listening to those that went.</summary>
    private void ObserveRecords(NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (INotifyPropertyChanged record in _observed.Keys)
                record.PropertyChanged -= OnRecordChanged;

            _observed.Clear();

            foreach (object item in Items)
                Observe(item);

            return;
        }

        // a move takes nothing away and brings nothing new
        if (e.Action == NotifyCollectionChangedAction.Move) return;

        if (e.OldItems is not null)
            foreach (object? item in e.OldItems)
                Forget(item);

        if (e.NewItems is not null)
            foreach (object? item in e.NewItems)
                Observe(item);
    }

    private void Observe(object? item)
    {
        if (item is not INotifyPropertyChanged record) return;

        if (_observed.TryGetValue(record, out int count))
        {
            _observed[record] = count + 1;
            return;
        }

        _observed[record] = 1;
        record.PropertyChanged += OnRecordChanged;
    }

    private void Forget(object? item)
    {
        if (item is not INotifyPropertyChanged record || !_observed.TryGetValue(record, out int count)) return;

        if (count > 1)
        {
            _observed[record] = count - 1;
            return;
        }

        _observed.Remove(record);
        record.PropertyChanged -= OnRecordChanged;
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        _form = FindForm();

        // the source is listened to only while the grid is in a form
        _dataSource?.OnAttached();
    }
}