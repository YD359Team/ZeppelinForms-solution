using System.Globalization;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZF_SharedLib.Models;

namespace ZF_SharedLib.Showcase;

/// <summary>Data in rows and columns: a plain table of strings and a data grid over
/// objects, with sorting, resizing and selection, move the selection. 
/// Shift+wheel scrolls a grid wider than the window sideways.</summary>
internal static class DataPage
{
    public static UIElement Create()
    {
        // ===== table =====

        var table = new Table
        {
            Columns =
            [
                new("#", GridLength.Fixed(40)) { Align = HorizontalContentAlignment.Right },
                new("Product", GridLength.Star()),
                new("Price", GridLength.Fixed(80)) { Align = HorizontalContentAlignment.Right },
                new("In stock", GridLength.Fixed(80)) { Align = HorizontalContentAlignment.Center },
            ],
            ShowAlternateRows = true,
            Size = new(480, float.NaN),
        };

        table.AddRow(["1", "Apple", "$0.50", "yes"]);
        table.AddRow(["2", "Tomato", "$0.35", "no"]);
        table.AddRow(["3", "Bread", "$1.00", "no"]);
        table.AddRow(["4", "Juice", "$2.00", "no"]);
        table.AddRow(["5", "Cola", "$1.00", "yes"]);

        var lines = new ToggleSwitch { Text = "Grid lines", IsOn = table.ShowGridLines };
        lines.Toggled += (_, _) => table.ShowGridLines = lines.IsOn;

        var stripes = new ToggleSwitch { Text = "Alternate rows", IsOn = true };
        stripes.Toggled += (_, _) => table.ShowAlternateRows = stripes.IsOn;

        // ===== data grid =====

        Label selection = Demo.Note("Select a row");

        var grid = new DataGridView
        {
            Size = new(float.NaN, 320),
            Columns =
            {
                // fixed, not Auto: the row number is short and predictable,
                // and Auto would be measured again on every scroll. An untyped
                // column: the record comes as an object and is cast
                new DataGridViewColumn
                {
                    Header = "#",
                    Width = GridLength.Fixed(40),
                    Align = HorizontalContentAlignment.Right,
                    CanSort = false,
                    Value = item => Array.IndexOf(Samples.Games, (Game)item) + 1,
                },
                // the same columns typed: the record and the value come as they are,
                // without casts, and an int or a float isn't boxed for every cell
                new DataGridViewColumn<Game, string>(game => game.Title)
                {
                    Header = "Game",
                    Width = GridLength.Star(2),
                },
                new DataGridViewColumn<Game, string>(game => game.Developer)
                {
                    Header = "Developer",
                    Width = GridLength.Star(2),
                },
                new DataGridViewColumn<Game, int>(game => game.Year)
                {
                    Header = "Year",
                    Width = GridLength.Fixed(60),
                    Align = HorizontalContentAlignment.Center,
                    // without a format an int would print as "2 004" in cultures
                    // with a group separator
                    Format = year => year.ToString(CultureInfo.InvariantCulture),
                },
                new DataGridViewColumn<Game, float>(game => game.Rating)
                {
                    Header = "Rating",
                    Width = GridLength.Fixed(70),
                    Align = HorizontalContentAlignment.Right,
                    Format = rating => rating.ToString("0.0", CultureInfo.CurrentCulture),
                },
                new DataGridViewColumn<Game, bool>(game => game.Completed)
                {
                    Header = "Completed",
                    Width = GridLength.Fixed(90),
                    Align = HorizontalContentAlignment.Center,
                    Format = completed => completed ? "Yes" : "No",
                },
            },
        };

        grid.Items.AddRange(Samples.Games);

        grid.SelectionChanged += (_, item) =>
            selection.Text = grid.SelectionUnit switch
            {
                DataGridViewSelectionUnit.Cell => $"{grid.SelectedCells.Count} cell(s) selected",
                DataGridViewSelectionUnit.FullColumn => $"{grid.SelectedColumns.Count} column(s) selected",
                _ when grid.SelectedIndices.Count > 1 => $"{grid.SelectedIndices.Count} games selected",
                _ => item is Game game ? $"{game.Title} — {game.Developer}, {game.Year}" : "Select a row",
            };

        // what a click selects and how many: Ctrl+click toggles, Shift+click extends,
        // Ctrl+A selects all and Ctrl+C copies the selection as a table
        var unit = new ComboBox { Items = { "Rows", "Cells", "Columns" }, SelectedIndex = 0 };
        unit.SelectionChanged += (_, _) => grid.SelectionUnit = (DataGridViewSelectionUnit)Math.Max(0, unit.SelectedIndex);

        var mode = new ComboBox { Items = { "Single", "Multiple", "Extended" }, SelectedIndex = 0 };
        mode.SelectionChanged += (_, _) => grid.SelectionMode = (SelectionMode)Math.Max(0, mode.SelectedIndex);

        Label sorted = Demo.Note("Click a header to sort, again to reverse");
        grid.SortChanged += (_, _) => sorted.Text = "Sorted: the selection stays on its game";

        var sortable = new ToggleSwitch { Text = "Sort by header click", IsOn = grid.CanSortByHeaderClick };
        sortable.Toggled += (_, _) => grid.CanSortByHeaderClick = sortable.IsOn;

        var resizable = new ToggleSwitch { Text = "Resize columns", IsOn = grid.CanResizeColumns };
        resizable.Toggled += (_, _) => grid.CanResizeColumns = resizable.IsOn;

        // every other row striped, as a style sheet would do it: DataGridView { ShowAlternateRows: true; }
        var gridStripes = new ToggleSwitch { Text = "Alternate rows" };
        gridStripes.Toggled += (_, _) => grid.ShowAlternateRows = gridStripes.IsOn;

        // a row's look by its record: the games not completed are dimmed. One shared
        // style, not one per call — the grid asks for every visible row on every frame
        var unfinished = new DataGridViewRowStyle { TextColor = new Color(150, 128, 128, 128) };

        var dimUnfinished = new ToggleSwitch { Text = "Dim unfinished" };
        dimUnfinished.Toggled += (_, _) =>
            grid.RowStyle = dimUnfinished.IsOn ? item => ((Game)item).Completed ? null : unfinished : null;

        var gridDemo = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
        };
        gridDemo.Children.AddRange([grid, Demo.Row(sortable, resizable, gridStripes, dimUnfinished), Demo.Row(unit, mode), selection, sorted]);

        return Demo.Page("Grids and tables", "A table of strings to show, a data grid over objects to work with.",
            Demo.Section("Table", Demo.Column(table, Demo.Row(lines, stripes)), "Table lays out strings; its columns are Auto, Fixed or Star like a Grid's"),
            Demo.Section("Data grid", gridDemo, "Each column reads its value from the object and formats it; rows, cells or columns are selected one at a time or many, with Ctrl and Shift"),
            Demo.Section("Editing", CreateEditableGrid(), "A column with SetValue is edited: F2, a double click or typing opens the editor of its kind, Enter and Tab commit, Escape cancels; a check box toggles with a click"));
    }

    private enum Priority { Low, Normal, High }

    /// <summary>A mutable record: the games above are records of init-only values,
    /// and a cell is edited by writing into its record.</summary>
    private sealed class Chore
    {
        public string Title { get; set; } = "";
        public Priority Priority { get; set; } = Priority.Normal;
        public DateTime Due { get; set; } = DateTime.Today;
        public int Hours { get; set; }
        public bool Done { get; set; }
        public IReadOnlyList<string> Tags { get; set; } = [];
    }

    private static UIElement CreateEditableGrid()
    {
        var grid = new DataGridView
        {
            Size = new(float.NaN, 220),
            SelectionUnit = DataGridViewSelectionUnit.Cell,
            Columns =
            {
                new DataGridViewColumn<Chore, string>(chore => chore.Title)
                {
                    Header = "Chore",
                    Width = GridLength.Star(2),
                    SetValue = (chore, title) => chore.Title = title,
                },
                new DataGridViewComboBoxColumn<Chore, Priority>(chore => chore.Priority)
                {
                    Header = "Priority",
                    Width = GridLength.Fixed(100),
                    SetValue = (chore, priority) => chore.Priority = priority,
                },
                new DataGridViewDateTimeColumn<Chore>(chore => chore.Due)
                {
                    Header = "Due",
                    Width = GridLength.Fixed(120),
                    SetValue = (chore, due) => chore.Due = due,
                },
                new DataGridViewNumericColumn<Chore, int>(chore => chore.Hours)
                {
                    Header = "Hours",
                    Width = GridLength.Fixed(80),
                    Minimum = 0,
                    Maximum = 40,
                    SetValue = (chore, hours) => chore.Hours = hours,
                },
                new DataGridViewCheckedListColumn<Chore, string>(chore => chore.Tags)
                {
                    Header = "Tags",
                    Width = GridLength.Star(),
                    Options = ["home", "work", "errand", "urgent"],
                    SetValue = (chore, tags) => chore.Tags = tags,
                },
                new DataGridViewCheckBoxColumn<Chore>(chore => chore.Done)
                {
                    Header = "Done",
                    SetValue = (chore, done) => chore.Done = done,
                },
            },
        };

        grid.Items.AddRange(
        [
            new Chore { Title = "Water the plants", Priority = Priority.Low, Hours = 1, Tags = ["home"] },
            new Chore { Title = "Write the release notes", Priority = Priority.High, Due = DateTime.Today.AddDays(2), Hours = 3, Tags = ["work"] },
            new Chore { Title = "Buy a new keyboard", Due = DateTime.Today.AddDays(7), Hours = 2, Tags = ["errand"] },
            new Chore { Title = "Fix the leaking tap", Priority = Priority.High, Hours = 2, Tags = ["home", "urgent"], Done = true },
        ]);

        Label changed = Demo.Note("Edit a cell");
        grid.CellValueChanged += (_, e) =>
            changed.Text = $"{e.Column.Header} of \"{((Chore)e.Item).Title}\" is now {e.Column.Value(e.Item) switch
            {
                IReadOnlyList<string> tags => string.Join(", ", tags),
                DateTime date => date.ToShortDateString(),
                { } value => value.ToString(),
                null => "empty",
            }}";

        var readOnly = new ToggleSwitch { Text = "Read-only" };
        readOnly.Toggled += (_, _) => grid.ReadOnly = readOnly.IsOn;

        return Demo.Column(grid, Demo.Row(readOnly), changed);
    }
}
