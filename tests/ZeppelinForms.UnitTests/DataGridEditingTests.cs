using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>0.17.0: cells are edited — in a control over the cell, or a check box in
/// place — and every value written goes through the grid's events.</summary>
[Collection("Platform")]
public class DataGridEditingTests
{
    private enum Status { Planned, Playing, Done }

    private sealed class Game
    {
        public string Title { get; set; } = "";
        public int Year { get; set; }
        public bool Completed { get; set; }
        public Status Status { get; set; }
        public DateTime Released { get; set; }
        public decimal Price { get; set; }
        public IReadOnlyList<string> Tags { get; set; } = [];
    }

    private const float RowHeight = 26f;
    private const float HeaderHeight = 30f;
    private const float ColumnWidth = 90f;

    private static (Form Form, DataGridView Grid) CreateGrid()
    {
        Keyboard.Reset();

        var grid = new DataGridView
        {
            RowHeight = RowHeight,
            HeaderHeight = HeaderHeight,
            Columns =
            {
                new DataGridViewColumn<Game, string>(game => game.Title)
                {
                    Header = "Title", Width = GridLength.Fixed(ColumnWidth), SetValue = (game, title) => game.Title = title,
                },
                new DataGridViewColumn<Game, int>(game => game.Year)
                {
                    Header = "Year", Width = GridLength.Fixed(ColumnWidth), SetValue = (game, year) => game.Year = year,
                },
                new DataGridViewCheckBoxColumn<Game>(game => game.Completed)
                {
                    Header = "Done", Width = GridLength.Fixed(ColumnWidth), SetValue = (game, done) => game.Completed = done,
                },
                new DataGridViewComboBoxColumn<Game, Status>(game => game.Status)
                {
                    Header = "Status", Width = GridLength.Fixed(ColumnWidth), SetValue = (game, status) => game.Status = status,
                },
            },
        };

        for (int i = 0; i < 4; i++)
            grid.Items.Add(new Game { Title = $"G{i}", Year = 2000 + i });

        var form = new Form { Size = new Size(500, 400), Content = grid };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();
        form.FocusElement(grid);

        return (form, grid);
    }

    private static Point CellPoint(DataGridView grid, int row, int column)
    {
        Point origin = grid.GetAbsolutePosition();

        return new Point(
            origin.X + grid.Padding.Left + column * ColumnWidth + ColumnWidth / 2,
            origin.Y + grid.Padding.Top + HeaderHeight + row * RowHeight + RowHeight / 2);
    }

    private static Game GameAt(DataGridView grid, int row) => grid.RowItem<Game>(row);

    [Fact]
    public void F2OpensATextBoxOverTheCellAndEnterWritesTheValue()
    {
        var (form, grid) = CreateGrid();
        grid.SelectedIndex = 1;
        grid.CurrentColumnIndex = 0;

        HeadlessInput.PressKey(form, Key.F2);

        TextBox editor = Assert.IsType<TextBox>(grid.EditingControl);
        Assert.Contains(editor, form.Overlays);
        Assert.Equal("G1", editor.Text);

        // the editor stands over its cell
        Point cell = CellPoint(grid, 1, 0);
        Assert.InRange(cell.X, editor.Position.X, editor.Position.X + ColumnWidth);
        Assert.InRange(cell.Y, editor.Position.Y, editor.Position.Y + RowHeight);

        // the whole value is selected: typing replaces it
        HeadlessInput.TypeText(form, "Renamed");
        HeadlessInput.PressKey(form, Key.Enter);

        Assert.False(grid.IsEditing);
        Assert.DoesNotContain(editor, form.Overlays);
        Assert.Equal("Renamed", GameAt(grid, 1).Title);

        // Enter goes down a row, as in a spreadsheet
        Assert.Equal(2, grid.SelectedIndex);
    }

    [Fact]
    public void TypingOnTheGridBeginsEditingWithWhatIsTyped()
    {
        var (form, grid) = CreateGrid();
        grid.SelectedIndex = 0;
        grid.CurrentColumnIndex = 1;

        HeadlessInput.TypeText(form, "1999");
        HeadlessInput.PressKey(form, Key.Enter);

        Assert.Equal(1999, GameAt(grid, 0).Year);
    }

    [Fact]
    public void ATextThatIsNotANumberKeepsTheEditorOpen()
    {
        var (form, grid) = CreateGrid();
        grid.BeginEdit(0, 1);

        HeadlessInput.TypeText(form, "soon");
        HeadlessInput.PressKey(form, Key.Enter);

        Assert.True(grid.IsEditing);
        Assert.Equal(2000, GameAt(grid, 0).Year);

        HeadlessInput.PressKey(form, Key.Escape);

        Assert.False(grid.IsEditing);
        Assert.Equal(2000, GameAt(grid, 0).Year);
    }

    [Fact]
    public void EscapeCancels()
    {
        var (form, grid) = CreateGrid();
        grid.BeginEdit(2, 0);

        HeadlessInput.TypeText(form, "Lost");
        HeadlessInput.PressKey(form, Key.Escape);

        Assert.Equal("G2", GameAt(grid, 2).Title);
        Assert.False(grid.IsEditing);
    }

    [Fact]
    public void TabGoesOnToTheNextCellWithAnEditor()
    {
        var (form, grid) = CreateGrid();
        grid.BeginEdit(0, 1);

        // past the check box, which has no editor, to the drop-down
        HeadlessInput.PressKey(form, Key.Tab);

        Assert.IsType<ComboBox>(grid.EditingControl);
        Assert.Equal(new DataGridViewCellIndex(0, 3), grid.EditingCell);
    }

    [Fact]
    public void AClickOnACheckBoxToggles()
    {
        var (form, grid) = CreateGrid();

        form.OnPointerDown(CellPoint(grid, 2, 2));
        form.OnPointerUp(CellPoint(grid, 2, 2));

        Assert.True(GameAt(grid, 2).Completed);
        Assert.False(grid.IsEditing);

        // Space on the current cell toggles it back
        HeadlessInput.PressKey(form, Key.Space);

        Assert.False(GameAt(grid, 2).Completed);
    }

    [Fact]
    public void AHandlerCanChangeOrRefuseTheValue()
    {
        var (_, grid) = CreateGrid();

        grid.CellValueChanging += (_, e) =>
        {
            if (e.NewValue is string title) e.NewValue = title.ToUpperInvariant();
            if (e.NewValue is int) e.Cancel = true;
        };

        int changed = 0;
        grid.CellValueChanged += (_, _) => changed++;

        Assert.True(grid.SetCellValue(0, 0, "quiet"));
        Assert.False(grid.SetCellValue(0, 1, 1990));

        Assert.Equal("QUIET", GameAt(grid, 0).Title);
        Assert.Equal(2000, GameAt(grid, 0).Year);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void ReadOnlyCellsAreNotEdited()
    {
        var (_, grid) = CreateGrid();

        grid.ReadOnly = true;
        Assert.False(grid.BeginEdit(0, 0));

        grid.ReadOnly = false;
        grid.Columns[0].ReadOnly = true;
        Assert.False(grid.BeginEdit(0, 0));
        Assert.True(grid.BeginEdit(0, 1));
    }

    [Fact]
    public void EditingASortedColumnMovesTheRowAndTheSelectionFollows()
    {
        var (_, grid) = CreateGrid();
        grid.SortBy(1);
        grid.SelectedIndex = 0;
        Game first = GameAt(grid, 0);

        grid.SetCellValue(0, 1, 2100);

        Assert.Same(first, GameAt(grid, 3));
        Assert.Equal(3, grid.SelectedIndex);
    }

    [Fact]
    public void RemovingTheEditedRecordClosesTheEditor()
    {
        var (form, grid) = CreateGrid();
        grid.BeginEdit(1, 0);
        UIElement editor = grid.EditingControl!;

        grid.Items.RemoveAt(1);

        Assert.False(grid.IsEditing);
        Assert.DoesNotContain(editor, form.Overlays);
    }

    // ===== the kinds of columns =====

    [Fact]
    public void AComboBoxColumnOffersTheMembersOfItsEnum()
    {
        var (_, grid) = CreateGrid();
        grid.BeginEdit(0, 3);

        ComboBox combo = Assert.IsType<ComboBox>(grid.EditingControl);
        Assert.Equal([Status.Planned, Status.Playing, Status.Done], combo.Items.Cast<Status>());

        combo.SelectedIndex = 2;
        grid.EndEdit();

        Assert.Equal(Status.Done, GameAt(grid, 0).Status);
    }

    [Fact]
    public void ADateColumnKeepsTheTimeOfDay()
    {
        var column = new DataGridViewDateTimeColumn<Game>(game => game.Released)
        {
            SetValue = (game, date) => game.Released = date,
        };

        var game = new Game { Released = new DateTime(2020, 5, 1, 14, 30, 0) };

        var picker = Assert.IsType<DateTimePicker>(column.CreateEditor(game, null));
        picker.SetValue(new DateTime(2021, 1, 2));

        Assert.True(column.TryGetEditedValue(picker, game, out object? value));
        Assert.Equal(new DateTime(2021, 1, 2, 14, 30, 0), value);
    }

    [Fact]
    public void ANumericColumnEditsInItsRange()
    {
        var column = new DataGridViewNumericColumn<Game, decimal>(game => game.Price)
        {
            Minimum = 0,
            Maximum = 100,
            DecimalPlaces = 2,
        };

        var game = new Game { Price = 9.99m };

        var numeric = Assert.IsType<NumericUpDown>(column.CreateEditor(game, null));
        Assert.Equal(9.99m, numeric.Value);

        numeric.Value = 250;

        Assert.True(column.TryGetEditedValue(numeric, game, out object? value));
        Assert.Equal(100m, value);
    }

    [Fact]
    public void ACheckedListColumnWritesTheCheckedOptionsInTheirOrder()
    {
        var column = new DataGridViewCheckedListColumn<Game, string>(game => game.Tags)
        {
            Options = ["co-op", "indie", "rpg"],
        };

        var game = new Game { Tags = ["rpg"] };

        Assert.Equal("rpg", column.TextOf(game));

        var list = Assert.IsType<CheckedComboBox>(column.CreateEditor(game, null));
        list.SetChecked(0, true);

        Assert.True(column.TryGetEditedValue(list, game, out object? value));
        Assert.Equal(["co-op", "rpg"], Assert.IsAssignableFrom<IReadOnlyList<string>>(value));
    }

    [Fact]
    public void AnImageColumnHasNoTextAndNoEditor()
    {
        var column = new DataGridViewImageColumn<Game>(_ => null);

        Assert.Equal(string.Empty, column.TextOf(new Game()));
        Assert.Null(column.CreateEditor(new Game(), null));
        Assert.False(column.CanSort);
    }
}
