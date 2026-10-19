# Data grid

`DataGridView` shows records in rows and columns: sorted by a header click, resized by
dragging a header's edge, selected by rows, cells or columns, and edited in place. Rows
are virtualized and cells are drawn rather than made of controls — a grid of a hundred
thousand rows costs what is in sight. A control is made only for the cell being edited.

```csharp
var grid = new DataGridView
{
    SelectionMode = SelectionMode.Extended,
    Columns =
    {
        new DataGridViewColumn<Customer, string>(c => c.Name) { Width = GridLength.Star(2) },
        new DataGridViewColumn<Customer, DateTime>(c => c.Since) { Format = d => d.ToString("d") },
        new DataGridViewNumericColumn<Customer, decimal>(c => c.Balance)
        {
            DecimalPlaces = 2,
            SetValue = (c, balance) => c.Balance = balance,
        },
        new DataGridViewCheckBoxColumn<Customer>(c => c.IsActive) { SetValue = (c, active) => c.IsActive = active },
    },
};

grid.DataSource = customers;   // an ObservableCollection<Customer>, a list, a query…
```

## Rows

The rows are `Items` — any objects; the columns know what to take out of them — or the
records of a [`DataSource`](Data-Binding#data-sources), which the grid then follows.

A record that implements `INotifyPropertyChanged` is redrawn when it changes, and the grid
sorts again if it is sorted by that column. A plain record changed in place needs
`grid.Refresh()` after the update — callable from any thread, and calls made before the
next frame are joined into one.

`RowItem(row)` and `RowItem<T>(row)` give the record of a row on screen: with sorting that
is not `Items[row]`. `CellValue(row, column)` and `CellText(row, column)` read one cell.

## Columns

A column takes the value out of a record with a delegate, never by reflection.

| Member | |
| ------ | - |
| `Header` | The header text |
| `Width` | `GridLength.Fixed(80)`, `GridLength.Star()`, `GridLength.Auto` (by the header and the rows in sight) |
| `Align` | The content alignment |
| `Format` | Value → text; null — `ToString()` with the current culture |
| `SetValue` | Writes an edited value back; null — the column is read-only |
| `Parse` | Text → value for a text column; null — the type's own parsing |
| `ReadOnly` | Not edited, even with `SetValue` |
| `CanSort`, `Comparer` | Sorting by the column |

### Typed and untyped

`DataGridViewColumn<TItem, TValue>` is typed: no casts in the delegates, and a number or a
date is neither boxed for every visible cell of every frame nor for every comparison of a
sort — the values sort as numbers, not as their text.

```csharp
new DataGridViewColumn<Process, long>(p => p.WorkingSet64)
{
    Format = bytes => $"{bytes / 1024 / 1024} MB",   // shown as text, sorted as a number
}
```

A typed column reading one member is headed by its name until `Header` is set —
`(p => p.WorkingSet64)` gets "WorkingSet64" — as a WinForms grid heads the columns it
makes from properties.

The untyped `DataGridViewColumn` takes the record and the value as objects; both kinds
go into the same `Columns`:

```csharp
new DataGridViewColumn { Header = "#", Value = item => ((Customer)item).Id }
```

A row whose record is not a `TItem` shows an empty cell in a typed column and sorts first.
A column whose delegate throws — usually a cast to the wrong type in an untyped column —
fails with an `InvalidOperationException` naming the column and the record's type.

### Kinds of columns

| Column | Value | Edited in |
| ------ | ----- | --------- |
| `DataGridViewColumn<TItem, TValue>` | any | a text box |
| `DataGridViewCheckBoxColumn<TItem>` | `bool` | in place: a click or Space toggles it |
| `DataGridViewComboBoxColumn<TItem, TValue>` | one of `Values`; for an enum every member by default | a combo box |
| `DataGridViewDateTimeColumn<TItem>` | `DateTime` (`DateFormat`) | a date picker; the time of day is kept |
| `DataGridViewNumericColumn<TItem, TValue>` | `int`, `double`, `decimal`… (`Minimum`, `Maximum`, `Step`, `DecimalPlaces`) | a numeric up-down |
| `DataGridViewCheckedListColumn<TItem, TOption>` | `IReadOnlyList<TOption>` of `Options`; for an enum every member | a checked combo box |
| `DataGridViewImageColumn<TItem>` | `Image?` (`Layout`) | not edited, not sorted |

The check boxes take the grid's `CheckColor` and `CheckGlyphColor`, which the themes set as
they do a `CheckBox`'s.

### Columns of your own

A column of another kind derives from `DataGridViewColumn<TItem, TValue>` and overrides:

| Override | |
| -------- | - |
| `DrawCell(g, bounds, item, paint)` | Draws the cell inside its padding; `paint` has the row's text color, font, selection and the grid |
| `CreateEditor(item, typedText)` | The control the cell is edited in; null — not edited in a control |
| `TryGetEditedValue(editor, item, out value)` | The value the editor holds; false keeps the editor open |
| `TryActivate(item, out value)` | A click or Space changes the value at once, as a check box does |

```csharp
public class RatingColumn<TItem> : DataGridViewColumn<TItem, int>
{
    // the base sets the required Value: a constructor of a derived column says so too
    [SetsRequiredMembers]
    public RatingColumn(Func<TItem, int> stars,
        [CallerArgumentExpression(nameof(stars))] string? expression = null)
        : base(stars, expression)
    {
    }

    protected override void DrawCell(Graphics g, Rectangle bounds, object item, DataGridViewCellPaint paint) =>
        g.DrawText(new string('★', CellStars(item)), bounds, paint.TextColor, paint.Font,
            HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

    private int CellStars(object item) => item is TItem record ? ValueOf(record) : 0;
}
```

## Selection

| Member | |
| ------ | - |
| `SelectionUnit` | What a click selects: `FullRow` (default), `Cell`, `FullColumn` |
| `SelectionMode` | How many: `Single` (default), `Multiple`, `Extended` |
| `SelectedIndex`, `CurrentColumnIndex` | The lead — where the keyboard is |
| `SelectedItem`, `SelectedItems`, `SelectedIndices` | The selected records and rows |
| `SelectedCells`, `SelectedColumns` | The selected cells (`DataGridViewCellIndex`) and columns |
| `IsRowSelected`, `IsCellSelected`, `IsColumnSelected` | Asked per row, cell or column |
| `SetRowSelected`, `SetCellSelected`, `SetColumnSelected` | From code — the one of the current unit |
| `SelectAll()`, `ClearSelection()` | |
| `SelectionChanged` | Any change of the selection or the lead |

The selection is kept by record: a sort, a row inserted above or a refresh keep the same
records selected, wherever they are now. A record removed from the rows leaves the
selection.

In `Extended` mode Ctrl+click toggles, Shift+click selects a range from the anchor — a
block of cells when selecting cells. Selecting columns, a click on a header selects the
column instead of sorting by it.

The keys: the arrows, PageUp/PageDown, Home/End move the lead and select; selecting cells
or columns Left/Right and Home/End move between columns, Ctrl+Home/End to the first and the
last row. Shift extends, Ctrl+A selects all, Space toggles in `Multiple` mode (Ctrl+Space in
`Extended`). **Ctrl+C** copies the selection as tab-separated text that a spreadsheet pastes
as a table; `CopyHeaders = true` puts the headers on the first line. `CopySelection()` and
`GetSelectionText()` do the same from code.

## Editing

A cell is editable when its column has `SetValue` and isn't `ReadOnly`, and the grid isn't
`ReadOnly`.

| Begins an edit | Ends it |
| -------------- | ------- |
| F2 | Enter — commits and goes down a row |
| A double click | Tab / Shift+Tab — commits and edits the next editable cell |
| Typing on a text column — the typed text replaces the value | Escape — cancels |
| `BeginEdit()`, `BeginEdit(row, column)` | A click elsewhere — commits; `EndEdit()`, `CancelEdit()` |

A value that isn't one — a number that doesn't parse — keeps the editor open. Every value
written, from an editor, a check box or `SetCellValue(row, column, value)`, goes through
two events:

```csharp
grid.CellValueChanging += (_, e) =>
{
    if (e.Column.Header == "Balance" && e.NewValue is decimal balance && balance < 0)
        e.Cancel = true;                 // or replace e.NewValue
};

grid.CellValueChanged += (_, e) => repository.Save((Customer)e.Item);
```

`CellEditing` comes before an editor opens and may cancel it. `IsEditing`, `EditingCell` and
`EditingControl` tell what is being edited.

## Sorting

A click on a header sorts by its column, a second click reverses the order. `SortBy(column,
descending)` and `ClearSort()` do it from code; `CanSortByHeaderClick` and a column's
`CanSort` turn it off. Sorting is a way of looking: `Items` keep their order. The sort is
stable — equal values keep their relative order.

## Looks

`RowHeight`, `HeaderHeight`, `CellPadding`, `ShowAlternateRows`, `AlternateRowColor`,
`SelectionColor`, `SelectedTextColor`, `RowHoverColor`, `HeaderColor`, `GridLineColor` — all
styled, so a style sheet sets them:

```css
DataGridView { ShowAlternateRows: true; RowHeight: 28; }
```

`RowStyle` gives a row its own look by its record — a process that stopped answering in
red, a total in bold:

```csharp
var stopped = new DataGridViewRowStyle { TextColor = Colors.Red };
grid.RowStyle = item => ((Process)item).Responding ? null : stopped;
```

`ScrollPastEnd` lets the grid scroll until the last row stands under the header.
