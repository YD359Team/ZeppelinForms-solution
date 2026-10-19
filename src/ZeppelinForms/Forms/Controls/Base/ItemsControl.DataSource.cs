using System.Collections;
using ZeppelinForms.Data;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>The items from a data source: a collection the list follows rather than
/// items put into <see cref="Items"/> by hand.</summary>
public partial class ItemsControl
{
    private DataSourceMirror? _dataSource;

    private DataSourceMirror DataSourceMirror => _dataSource ??= new DataSourceMirror(this, (ItemCollection)Items);

    /// <summary>The items the list shows: a list, an array, a query, an
    /// <c>ObservableCollection&lt;T&gt;</c>, a <c>BindingList&lt;T&gt;</c>, or a
    /// <see cref="DataSource{T}"/> — a filtered and ordered view of one. Null — the items
    /// are what <see cref="Items"/> holds.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="Items"/> becomes a mirror of the source that only the source changes.
    /// A source that tells about its changes is followed, even from another thread —
    /// the items change on the UI thread; any other is read when set and on
    /// <see cref="RefreshDataSource"/>.
    /// </para>
    /// <para>A null in the source has no item. Set to null, the items stay as they
    /// were, and <see cref="Items"/> is the list's own again.</para>
    /// </remarks>
    public IEnumerable? DataSource
    {
        get => _dataSource?.Source;
        set
        {
            if (ReferenceEquals(value, _dataSource?.Source)) return;

            ((ItemCollection)Items).IsBound = value is not null;
            DataSourceMirror.SetSource(value);
        }
    }

    /// <summary>Read the data source again: after a source that doesn't tell about its
    /// changes — a list, an array — changed.</summary>
    public void RefreshDataSource() => _dataSource?.Resync();

    protected override void OnAttached()
    {
        base.OnAttached();

        // the source is listened to only while the list is in a form
        _dataSource?.OnAttached();
    }

    protected override void OnDetached()
    {
        _dataSource?.OnDetached();

        base.OnDetached();
    }
}
