using System.Collections;
using ZeppelinForms.Data;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>The rows from a data source: a collection the grid follows rather than
/// rows put into <see cref="Items"/> by hand.</summary>
public partial class DataGridView
{
    private DataSourceMirror? _dataSource;

    private DataSourceMirror DataSourceMirror => _dataSource ??= new DataSourceMirror(this, (ItemCollection)Items);

    /// <summary>The records the rows show: a list, an array, a query, an
    /// <c>ObservableCollection&lt;T&gt;</c>, a <c>BindingList&lt;T&gt;</c>, or a
    /// <see cref="DataSource{T}"/> — a filtered and ordered view of one. Null — the rows
    /// are what <see cref="Items"/> holds.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="Items"/> becomes a mirror of the source that only the source changes.
    /// A source that tells about its changes is followed, even from another thread —
    /// the rows change on the UI thread; any other is read when set and on
    /// <see cref="RefreshDataSource"/>. The selection keeps to its records through it all.
    /// </para>
    /// <para>
    /// A null in the source has no row. Set to null, the rows stay as they were, and
    /// <see cref="Items"/> is the grid's own again.
    /// </para>
    /// </remarks>
    public IEnumerable? DataSource
    {
        get => _dataSource?.Source;
        set
        {
            if (ReferenceEquals(value, _dataSource?.Source)) return;

            // the cell being edited may be of a record the new source hasn't
            CancelEdit();

            var items = (ItemCollection)Items;

            items.IsBound = value is not null;
            DataSourceMirror.SetSource(value);
        }
    }

    /// <summary>Read the data source again: after a source that doesn't tell about its
    /// changes — a list, an array — changed.</summary>
    public void RefreshDataSource()
    {
        _dataSource?.Resync();
        Refresh();
    }
}
