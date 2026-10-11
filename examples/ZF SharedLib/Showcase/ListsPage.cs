using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZF_SharedLib.Models;

namespace ZF_SharedLib.Showcase;

/// <summary>Lists: one choice or many, check boxes in the rows, rows of any look,
/// ten thousand rows at no cost, and a menu laid out as a list.</summary>
internal static class ListsPage
{
    public static UIElement Create()
    {
        // ===== selection =====

        Label single = Demo.Note("Click a game");
        var one = List(SelectionMode.Single);
        one.Items.AddRange(Samples.Games.Select(g => g.Title));
        one.SelectionChanged += (_, _) => single.Text = one.SelectedIndex >= 0 ? $"Chosen: {one.Items[one.SelectedIndex]}" : "Click a game";

        Label several = Demo.Note("Ctrl adds a row, Shift a range");
        var many = List(SelectionMode.Extended);
        many.Items.AddRange(Samples.Games.Select(g => g.Title));
        many.SelectionChanged += (_, _) => several.Text = $"{many.SelectedIndices.Count} chosen";

        var selection = Demo.Row(
            Demo.Column(Demo.Labeled("Single", one), single),
            Demo.Column(Demo.Labeled("Extended", many), several));

        // ===== check boxes =====

        Label checkedCount = Demo.Note("Nothing checked");
        var todo = new CheckedListBox { Size = new(260, 200), OverflowY = Overflow.Auto, ToggleOnRowClick = true };
        todo.Items.AddRange(["Buy milk", "Write the README", "Fix the scroll bar", "Answer the issues", "Release 0.15", "Rest"]);
        todo.ItemCheckedChanged += (_, _) => checkedCount.Text = $"{todo.CheckedIndices.Count} of {todo.Items.Count} done";

        // ===== templates =====

        var rich = new ListBox
        {
            Size = new(320, 240),
            OverflowY = Overflow.Auto,
            ItemTemplate = item => GameRow((Game)item),
        };
        rich.Items.AddRange(Samples.Games);

        // ===== virtualization =====

        object[] numbers = [.. Enumerable.Range(1, 10_000).Select(i => (object)i)];

        var huge = new VirtualizingStackPanel
        {
            Size = new(260, 240),
            ItemHeight = 28,
            ItemsSource = numbers,
            ItemTemplate = item => new Label
            {
                Text = $"Row {item:N0}",
                Padding = new(10, 0),
                VerticalContentAlign = VerticalContentAlignment.Center,
            },
            OverflowY = Overflow.Auto,
        };

        // ===== menu =====

        Label invoked = Demo.Note("Run a command");
        var menu = new MenuList
        {
            Size = new(220, float.NaN),
            Items =
            {
                new MenuItem { Text = "New" },
                new MenuItem { Text = "Open…" },
                new MenuItem { Text = "Save" },
                MenuItem.Separator,
                new MenuItem { Text = "Print", IsEnabled = false },
                new MenuItem { Text = "Delete" },
            },
        };
        menu.ItemInvoked += (_, item) => invoked.Text = $"Ran: {item.Text}";

        return Demo.Page("Lists", "Lists of strings, of objects in rows of their own look, of thousands of rows.",
            Demo.Section("Selection", selection, "SelectionMode: Single, Multiple (every click toggles) or Extended (Ctrl and Shift)"),
            Demo.Section("Check boxes", Demo.Column(todo, checkedCount), "ToggleOnRowClick: a click anywhere on the row checks it"),
            Demo.Section("Item template", rich, "ItemTemplate turns each item into the element that shows it"),
            Demo.Section("Ten thousand rows", huge, "VirtualizingStackPanel makes rows only for the visible part: scroll as fast as you like"),
            Demo.Section("Menu list", Demo.Column(menu, invoked), "The arrows move along it and skip the separator and the disabled item"));
    }

    private static ListBox List(SelectionMode mode) => new()
    {
        SelectionMode = mode,
        Size = new(240, 200),
        OverflowY = Overflow.Auto,
    };

    /// <summary>A game in two lines: the title over its developer and year, with the
    /// rating at the end.</summary>
    private static UIElement GameRow(Game game)
    {
        var text = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        text.Children.Add(new Label { Text = game.Title, TextStyle = TextStyle.BodyStrong });
        text.Children.Add(Demo.Note($"{game.Developer}, {game.Year}"));

        var rating = new Label
        {
            Text = game.Rating.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture),
            Padding = new(8, 2),
            CornerRadius = new(10),
            Background = game.Rating >= 9f ? new Color(255, 0x1B, 0x8A, 0x3C) : new Color(255, 0x6C, 0x75, 0x7D),
            TextColor = Colors.White,
        };

        rating.Docking = Dock.Right;
        text.Docking = Dock.Fill;

        var row = new DockPanel { Padding = new(10, 6) };
        row.Children.AddRange([rating, text]);
        return row;
    }
}
