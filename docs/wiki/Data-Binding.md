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

## The data context and MVVM

`DataContext` is the object an element's bindings start from — a view model. Set on an
element, it is that element's and everything's below it; not set, it is the parent's, up
to the form's `Form.DataContext`. Null means "the parent's".

```csharp
var form = new OrderForm { DataContext = new OrderViewModel(order) };
```

A change reaches the bindings at once — a data context set later, set on an ancestor or
the form, or an element moved under another — and `DataContextChanged` is raised where
the effective one changed.

### Typed bindings

In C# a binding to the data context is a lambda: no property names, no reflection.

```csharp
status.Bind(Label.TextProperty, (OrderViewModel vm) => vm.Status);
name.Bind(TextBox.TextProperty, (OrderViewModel vm) => vm.Name, (vm, value) => vm.Name = value);

// a source of its own rather than the data context
total.Bind(Label.TextProperty, cart, c => c.Total.ToString("C"));
```

With a setter the binding goes both ways. Every `PropertyChanged` of the view model reads
the value again, and the element is written only when it changed. A data context that
isn't the lambda's type gives no value: the property's default.

### Paths

A path of several steps follows every object on the way: `Customer.Name` reads the
view model's customer, follows it, and a new customer set on the view model is read and
followed instead. A null on the way gives the property's default. Markup compiles
`{Bind Customer.Name}` into such a path; from C# it is a `BindingPath` — each step a name
and a compiled read:

```csharp
box.Bind(TextBox.TextProperty,
    new BindingPath("Customer.Name", typeof(OrderViewModel), typeof(string),
        new BindingStep("Customer", vm => ((OrderViewModel)vm).Customer),
        new BindingStep("Name", c => ((Customer)c).Name))
    {
        Set = (c, value) => ((Customer)c).Name = (string)value!,
    },
    BindingMode.TwoWay);
```

A property that isn't styled — a button's `Command` — is kept set by `BindValue`, one way.

### In markup

`x:DataType` says what the data context is, for the element and everything inside it; an
inner one is for its own part. `{Bind Path}` then reads from the data context, checked at
build time — a property that isn't there, a `Mode=TwoWay` of a property without a setter,
a command that isn't one are ZF2008 at their line.

```xml
<StackPanel Class="MyApp.ProfileCard" xmlns:vm="using:MyApp.ViewModels" x:DataType="vm:ProfileViewModel">
  <Label Text="{Bind Greeting}"/>
  <TextBox Text="{Bind Name, Mode=TwoWay}"/>
  <TextBox Text="{Bind Address.City, Mode=TwoWay}"/>
  <PrimaryButton Text="Save" Command="{Bind Save}"/>
</StackPanel>
```

A path that starts with the name of an element binds to it, as before:
`{Bind NameBox.Text}`. The data context can be made in the markup too:
`<StackPanel.DataContext><vm:ProfileViewModel/></StackPanel.DataContext>`.

### Converters, formats and values where there is none

A binding may do something with the value on its way: convert it, make it text, or write a
value of its own where there is none. In markup they are options after the path, as WPF
and Avalonia write them:

```xml
<Loader IsVisible="{Bind IsIdle, Converter=BoolConverters.Not}"/>
<Label Text="{Bind Total, StringFormat='{0:N2} kg'}"/>
<Label Text="{Bind Customer.Name, FallbackValue=—}"/>
<Label Text="{Bind Note, TargetNullValue=(no note)}"/>
<Label Text="{Bind Count, Converter=Plural, ConverterParameter=item}"/>
```

| Option | |
| ------ | - |
| `Converter` | An `IValueConverter`: a property or a field of the class, `Type.Member` of another type, or one of the framework's — `BoolConverters.Not`, `ObjectConverters.IsNull`/`IsNotNull`, `StringConverters.IsNullOrEmpty`/`IsNotNullOrEmpty` |
| `ConverterParameter` | Given to the converter, as text |
| `FallbackValue` | Written when the path gives no value — a null on the way, a stream with nothing yet — or the value can't be made the property's type |
| `TargetNullValue` | Written when the value is null; both ways, the element's value equal to it is null at the source |
| `StringFormat` | A composite format, `'{0:N2} kg'`, or the value's own, `N2` |

A value with a comma goes in single quotes. The values are of the property's type, written
as its attribute would be. From C# they are a `BindingOptions`:

```csharp
hint.Bind(UIElement.IsVisibleProperty, path, BindingMode.OneWay,
    new BindingOptions { Converter = BoolConverters.Not });

var dozens = new FuncValueConverter<int, string>(n => $"{n * 12}", text => int.Parse(text!) / 12);
```

A converter returns `Binding.UnsetValue` for "no value" — the fallback is written — and
`Binding.DoNothing` to leave the other end as it is. Whatever it returns is made the
property's type as any bound value: a number for a text property becomes its text.

### Tasks and observables

`^` after a property of a `Task<T>` or an `IObservable<T>` reads what it gives — the task's
result, the observable's latest value — and the path goes on from there:

```xml
<Label Text="{Bind Profile^.Name, FallbackValue=Loading…}"/>
<Label Text="{Bind Price^, StringFormat=C}"/>
```

Until there is a value the path has none: the fallback, or the property's default. The value
is written on the element's UI thread wherever it came from. A failed or cancelled task gives
none. The binding subscribes while the stream is on the path, and lets it go when the property
gives another one, the path breaks before it or the binding goes. A stream can't be written:
such a binding is one way. From C# the step says it: `new BindingStep("Profile", …) { Stream
= StreamOperator.FromTask<Customer>() }`.

### Asynchronous commands

`AsyncCommand` is a command whose action is asynchronous — a download, a save:

```csharp
Load = new AsyncCommand(async token => Orders = await _repository.LoadAsync(token));
```

While a run goes on, the command can't run again — its buttons are disabled — unless
`AllowConcurrentExecutions`; `IsRunning` says it, for a spinner bound to it. `Cancel()` or
`CancelCommand` on a button cancels the run's token, and a run ended by it is no error. An
exception of the action goes where an `async void` handler's goes: to the UI thread and
`App.UnhandledException`; awaiting `ExecuteAsync` or `ExecutionTask` observes it instead.
`Gesture` is its shortcut, as a `Command`'s.

### View models

The bindings need `INotifyPropertyChanged` for changes and `ICommand` for commands —
nothing of the framework's. A view model of CommunityToolkit.Mvvm works as it is:

```csharp
public partial class ProfileViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() { /* … */ }

    private bool CanSave() => Name.Length > 0;
}
```

A computed property raises `PropertyChanged` for itself when what it reads changes, as any
MVVM binding needs. The showcase's `ProfileViewModel` and `ProfileCard.zfml` are a whole
example written by hand.

A view model may raise `PropertyChanged` on any thread — set from a worker, a timer, a
socket's callback. The element is written on its form's UI thread: the change is queued
there, once for all the changes told before its turn, and the binding reads its whole path
again. A command's `CanExecuteChanged` raised on a worker enables its button the same way.
An element that is in no form is written where it is told.

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
