using System.Globalization;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZF_SharedLib.Models;

namespace ZF_SharedLib.Showcase;

/// <summary>Data in rows and columns: a plain table of strings and a data grid over
/// objects, with sorting, resizing and selection.</summary>
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
                // and Auto would be measured again on every scroll
                new DataGridViewColumn
                {
                    Header = "#",
                    Width = GridLength.Fixed(40),
                    Align = HorizontalContentAlignment.Right,
                    CanSort = false,
                    Value = item => Array.IndexOf(Samples.Games, (Game)item) + 1,
                },
                new DataGridViewColumn
                {
                    Header = "Game",
                    Width = GridLength.Star(2),
                    Value = item => ((Game)item).Title,
                },
                new DataGridViewColumn
                {
                    Header = "Developer",
                    Width = GridLength.Star(2),
                    Value = item => ((Game)item).Developer,
                },
                new DataGridViewColumn
                {
                    Header = "Year",
                    Width = GridLength.Fixed(60),
                    Align = HorizontalContentAlignment.Center,
                    Value = item => ((Game)item).Year,
                    // without a format an int would print as "2 004" in cultures
                    // with a group separator
                    Format = value => ((int)value!).ToString(CultureInfo.InvariantCulture),
                },
                new DataGridViewColumn
                {
                    Header = "Rating",
                    Width = GridLength.Fixed(70),
                    Align = HorizontalContentAlignment.Right,
                    Value = item => ((Game)item).Rating,
                    Format = value => ((float)value!).ToString("0.0", CultureInfo.CurrentCulture),
                },
                new DataGridViewColumn
                {
                    Header = "Completed",
                    Width = GridLength.Fixed(90),
                    Align = HorizontalContentAlignment.Center,
                    Value = item => ((Game)item).Completed,
                    Format = value => (bool)value! ? "Yes" : "No",
                },
            },
        };

        grid.Items.AddRange(Samples.Games);

        grid.SelectionChanged += (_, item) =>
            selection.Text = item is Game game
                ? $"{game.Title} — {game.Developer}, {game.Year}"
                : "Select a row";

        Label sorted = Demo.Note("Click a header to sort, again to reverse");
        grid.SortChanged += (_, _) => sorted.Text = "Sorted: the selection stays on its game";

        var sortable = new ToggleSwitch { Text = "Sort by header click", IsOn = grid.CanSortByHeaderClick };
        sortable.Toggled += (_, _) => grid.CanSortByHeaderClick = sortable.IsOn;

        var resizable = new ToggleSwitch { Text = "Resize columns", IsOn = grid.CanResizeColumns };
        resizable.Toggled += (_, _) => grid.CanResizeColumns = resizable.IsOn;

        var gridDemo = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
        };
        gridDemo.Children.AddRange([grid, Demo.Row(sortable, resizable), selection, sorted]);

        return Demo.Page("Grids and tables", "A table of strings to show, a data grid over objects to work with.",
            Demo.Section("Table", Demo.Column(table, Demo.Row(lines, stripes)), "Table lays out strings; its columns are Auto, Fixed or Star like a Grid's"),
            Demo.Section("Data grid", gridDemo, "Each column reads its value from the object and formats it; the arrows, Home and End move the selection"));
    }
}
