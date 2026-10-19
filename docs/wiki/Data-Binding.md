# Data binding

Two kinds: a **binding** links one property of an element to one property of an object; a
**data source** gives a list control — a data grid, a list box, a combo box — the records
it shows, and keeps them in step.

## Bindings

Any [styled property](Styles-and-Themes#styled-properties) can be bound to a property of
any object:

```csharp
nameBox.Bind(TextBox.TextProperty, user, nameof(User.Name), BindingMode.TwoWay);
statusLabel.Bind(Label.TextProperty, user, nameof(User.Status));
saveButton.Bind(UIElement.IsEnabledProperty, user, nameof(User.HasChanges));
```

`OneWay` follows the source; `TwoWay` also writes back. A source implementing
`INotifyPropertyChanged` pushes its changes; without it a binding reads the source once,
when it is made.

A binding holds its element weakly: a model that outlives a window does not keep the window
alive. `Unbind` drops one property, `UnbindAll` all of them, and `ClearValue` the binding
with the value. Assigning a bound property from code wins over the binding: in `TwoWay` the
value goes to the source, in `OneWay` the binding is broken — otherwise the next change of
the source would overwrite what was just written.

## Data sources

`DataSource` on `DataGridView`, `ListBox` (any `ItemsControl`) and `ComboBox` takes the
records to show:

| Source | Followed |
| ------ | -------- |
| `ObservableCollection<T>`, any `INotifyCollectionChanged` | Change by change: rows come and go, the selection and the rows in sight stay |
| `BindingList<T>`, any `IBindingList` | Change by change |
| A list, an array, a query | Read when set and on `RefreshDataSource()` |
| [`DataSource<T>`](#datasourcet) | A filtered and ordered view of a collection |

```csharp
var customers = new ObservableCollection<Customer>(repository.Load());
grid.DataSource = customers;

customers.Add(new Customer("Zeppelin"));   // a row appears
```

With a data source set, **`Items` is a mirror of it** that only the source changes: adding to
`Items` throws an `InvalidOperationException` instead of quietly going out of step with the
source. Set `DataSource = null` to make `Items` the control's own again; the rows stay as
they were.

A null in the source has no row.

### Threads

A collection may be changed on another thread — a worker filling it, a timer polling. The
control doesn't touch its items there: the change is queued to its UI thread, once for a
burst of changes, and the items are compared with the source there. Change the collection
on one thread at a time.

### Lifetime

A control listens to its source only while it is in a form: the source's events would hold
the control, and a long-lived model would hold every window that ever showed it. Coming back
into a form, the control reads the source again and catches up.

## DataSource\<T>

`DataSource<T>` (namespace `ZeppelinForms.Data`) is a typed view of a collection: its records
that pass a filter, in an order — while the collection itself stays as it is. Several views of
one collection each keep their own.

```csharp
var due = new DataSource<Chore>(chores) { Filter = chore => !chore.Done };
grid.DataSource = due;

search.TextChanged += (_, _) =>
    due.Filter = string.IsNullOrEmpty(search.Text)
        ? null
        : chore => chore.Title.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase);
```

| Member | |
| ------ | - |
| `Source` | The collection |
| `Filter` | Which records show; null — all. Setting it rebuilds the view |
| `Comparer` | The order; null — the collection's own |
| `Refresh()` | Rebuild: after a plain collection changed, or a record changed so that it passes the filter now or moves in the order |
| `Count`, indexer, enumeration | The view as it is |
| `Dispose()` | Stop following the collection |

A view with neither a filter nor an order passes the collection's changes on as they are, so
a list keeps its selection and rows through them; a filtered or ordered one is rebuilt.
When a rebuild changes few rows — a filter narrowed by one more keystroke — the control
changes only those, so a list box keeps its selection on the records that stay; when it
changes many, the rows are replaced at once.

A data grid sorted by a header click orders its rows on top of the view, as a way of
looking, and leaves the view's `Comparer` be.

The view is read-only through `IList`: change the collection, and the view follows.
