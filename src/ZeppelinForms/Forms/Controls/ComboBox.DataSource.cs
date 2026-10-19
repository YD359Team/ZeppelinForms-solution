using System.Collections;
using ZeppelinForms.Data;

namespace ZeppelinForms.Forms.Controls;

/// <summary>The items from a data source: a collection the combo box follows rather
/// than items put into <see cref="Items"/> by hand.</summary>
public partial class ComboBox
{
    private DataSourceMirror? _dataSource;

    /// <summary>The items to pick from: a list, an array, a query, an
    /// <c>ObservableCollection&lt;T&gt;</c>, a <c>BindingList&lt;T&gt;</c>, or a
    /// <see cref="DataSource{T}"/>. Null — the items are what <see cref="Items"/> holds.</summary>
    /// <remarks>
    /// <see cref="Items"/> is then filled from the source and kept in step with it —
    /// change the source, not the list. The picked item stays picked while it is in the
    /// source, wherever it moves; gone from it, nothing is picked.
    /// </remarks>
    public IEnumerable? DataSource
    {
        get => _dataSource?.Source;
        set
        {
            if (ReferenceEquals(value, _dataSource?.Source)) return;

            _dataSource ??= new DataSourceMirror(this, new ListSink(this), OnDataSourceChanged);
            _dataSource.SetSource(value);
        }
    }

    /// <summary>Read the data source again: after a source that doesn't tell about its
    /// changes — a list, an array — changed.</summary>
    public void RefreshDataSource() => _dataSource?.Resync();

    /// <summary>The item picked before the items changed: the pick follows it.</summary>
    private object? _pickedBeforeChange;
    private bool _pickRemembered;

    private void RememberPick()
    {
        if (_pickRemembered) return;

        _pickedBeforeChange = SelectedItem;
        _pickRemembered = true;
    }

    private void OnDataSourceChanged()
    {
        object? picked = _pickedBeforeChange;

        _pickedBeforeChange = null;
        _pickRemembered = false;

        int index = picked is null ? -1 : Items.FindIndex(item => ReferenceEquals(item, picked));

        // the same item on another row: the pick moves with it quietly — nothing
        // the user picked has changed
        if (index >= 0)
            _selectedIndex = index;
        else
            SelectedIndex = -1;

        // the width is measured from the texts
        Invalidate();
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        // the source is listened to only while the combo box is in a form
        _dataSource?.OnAttached();
    }

    protected override void OnDetached()
    {
        _dataSource?.OnDetached();

        base.OnDetached();
    }

    /// <summary>Writes into <see cref="Items"/>, remembering the pick first: an index
    /// into a list that changes under it would point at another item.</summary>
    private sealed class ListSink(ComboBox owner) : IItemsSink
    {
        public IReadOnlyList<object> Current => owner.Items;

        public void Insert(int index, object item)
        {
            owner.RememberPick();
            owner.Items.Insert(index, item);
        }

        public void RemoveAt(int index)
        {
            owner.RememberPick();
            owner.Items.RemoveAt(index);
        }

        public void Replace(int index, object item)
        {
            owner.RememberPick();
            owner.Items[index] = item;
        }

        public void Move(int from, int to)
        {
            owner.RememberPick();

            object item = owner.Items[from];
            owner.Items.RemoveAt(from);
            owner.Items.Insert(to, item);
        }

        public void ResetTo(IReadOnlyList<object> items)
        {
            owner.RememberPick();
            owner.Items.Clear();
            owner.Items.AddRange(items);
        }
    }
}
