using Xunit;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>0.17.0: the grid selects rows, cells or columns — one, a toggled set or
/// ranges — and keeps the selection by record.</summary>
[Collection("Platform")]
public class DataGridSelectionTests
{
    private sealed record Person(string Name, int Age, string City);

    private const float RowHeight = 26f;
    private const float HeaderHeight = 30f;
    private const float ColumnWidth = 100f;

    private sealed class FakeClipboard : IClipboard
    {
        public string? Text { get; private set; }

        public string? GetText() => Text;

        public void SetText(string text) => Text = text;
    }

    private static (Form Form, DataGridView Grid) CreateGrid(
        DataGridViewSelectionUnit unit = DataGridViewSelectionUnit.FullRow,
        SelectionMode mode = SelectionMode.Extended,
        int rows = 6)
    {
        Keyboard.Reset();

        var grid = new DataGridView
        {
            RowHeight = RowHeight,
            HeaderHeight = HeaderHeight,
            SelectionUnit = unit,
            SelectionMode = mode,
            Columns =
            {
                new DataGridViewColumn<Person, string>(person => person.Name) { Header = "Name", Width = GridLength.Fixed(ColumnWidth) },
                new DataGridViewColumn<Person, int>(person => person.Age) { Header = "Age", Width = GridLength.Fixed(ColumnWidth) },
                new DataGridViewColumn<Person, string>(person => person.City) { Header = "City", Width = GridLength.Fixed(ColumnWidth) },
            },
        };

        for (int i = 0; i < rows; i++)
            grid.Items.Add(new Person($"P{i}", 20 + i, $"C{i}"));

        var form = new Form { Size = new Size(500, 400), Content = grid };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, grid);
    }

    /// <summary>The middle of a cell, in the form's coordinates.</summary>
    private static Point CellPoint(DataGridView grid, int row, int column)
    {
        Point origin = grid.GetAbsolutePosition();

        return new Point(
            origin.X + grid.Padding.Left + column * ColumnWidth + ColumnWidth / 2,
            origin.Y + grid.Padding.Top + HeaderHeight + row * RowHeight + RowHeight / 2);
    }

    private static Point HeaderPoint(DataGridView grid, int column)
    {
        Point origin = grid.GetAbsolutePosition();

        return new Point(
            origin.X + grid.Padding.Left + column * ColumnWidth + ColumnWidth / 2,
            origin.Y + grid.Padding.Top + HeaderHeight / 2);
    }

    private static void Click(Form form, Point at, KeyModifiers held = KeyModifiers.None)
    {
        // a click carries no modifiers: the grid reads the keyboard's state
        if (held.HasFlag(KeyModifiers.Shift)) form.OnKeyDown(Key.LeftShift, held, false);
        if (held.HasFlag(KeyModifiers.Control)) form.OnKeyDown(Key.LeftControl, held, false);

        form.OnPointerDown(at);
        form.OnPointerUp(at);

        Keyboard.Reset();
    }

    // ===== rows =====

    [Fact]
    public void ACtrlClickAddsARowAndAShiftClickSelectsTheRange()
    {
        var (form, grid) = CreateGrid();

        Click(form, CellPoint(grid, 1, 0));
        Click(form, CellPoint(grid, 3, 0), KeyModifiers.Control);

        Assert.Equal([1, 3], grid.SelectedIndices);
        Assert.Equal(3, grid.SelectedIndex);

        // the anchor is the last row clicked without Shift
        Click(form, CellPoint(grid, 5, 0), KeyModifiers.Shift);

        Assert.Equal([3, 4, 5], grid.SelectedIndices);
        Assert.Equal("P5", grid.RowItem<Person>(grid.SelectedIndex).Name);
    }

    [Fact]
    public void ASingleSelectionIgnoresTheModifiers()
    {
        var (form, grid) = CreateGrid(mode: SelectionMode.Single);

        Click(form, CellPoint(grid, 1, 0));
        Click(form, CellPoint(grid, 3, 0), KeyModifiers.Control);

        Assert.Equal([3], grid.SelectedIndices);
    }

    [Fact]
    public void MultipleTogglesRowsWithPlainClicks()
    {
        var (form, grid) = CreateGrid(mode: SelectionMode.Multiple);

        Click(form, CellPoint(grid, 0, 0));
        Click(form, CellPoint(grid, 2, 0));
        Click(form, CellPoint(grid, 0, 0));

        Assert.Equal([2], grid.SelectedIndices);
    }

    [Fact]
    public void TheSelectedRowsFollowTheirRecordsThroughASortAndARemoval()
    {
        var (_, grid) = CreateGrid();
        grid.SetRowSelected(1, true);
        grid.SetRowSelected(4, true);

        object first = grid.RowItem(1);
        object second = grid.RowItem(4);

        grid.SortBy(1, descending: true);

        Assert.Equal([first, second], grid.SelectedItems.OrderBy(record => grid.Items.IndexOf(record)));

        grid.Items.Remove(first);

        Assert.Equal([second], grid.SelectedItems);
    }

    [Fact]
    public void ShiftDownExtendsAndCtrlASelectsEverything()
    {
        var (form, grid) = CreateGrid();
        form.FocusElement(grid);
        grid.SelectedIndex = 2;

        HeadlessInput.PressKey(form, Key.Down, KeyModifiers.Shift);
        HeadlessInput.PressKey(form, Key.Down, KeyModifiers.Shift);

        Assert.Equal([2, 3, 4], grid.SelectedIndices);

        HeadlessInput.PressKey(form, Key.A, KeyModifiers.Control);

        Assert.Equal(6, grid.SelectedIndices.Count);
    }

    [Fact]
    public void SettingSelectedIndexSelectsThatRowAlone()
    {
        var (_, grid) = CreateGrid();
        grid.SelectAll();

        grid.SelectedIndex = 2;

        Assert.Equal([2], grid.SelectedIndices);

        grid.SelectedIndex = -1;

        Assert.Empty(grid.SelectedIndices);
        Assert.Null(grid.SelectedItem);
    }

    [Fact]
    public void SelectingTheSameRowAgainRaisesNothing()
    {
        var (_, grid) = CreateGrid();
        grid.SelectedIndex = 2;

        int raised = 0;
        grid.SelectionChanged += (_, _) => raised++;

        grid.SelectedIndex = 2;

        Assert.Equal(0, raised);
    }

    // ===== cells =====

    [Fact]
    public void ACellClickSelectsTheCellAndAShiftClickABlock()
    {
        var (form, grid) = CreateGrid(DataGridViewSelectionUnit.Cell);

        Click(form, CellPoint(grid, 1, 0));

        Assert.True(grid.IsCellSelected(1, 0));
        Assert.False(grid.IsCellSelected(1, 1));
        Assert.Equal(0, grid.CurrentColumnIndex);

        Click(form, CellPoint(grid, 2, 1), KeyModifiers.Shift);

        Assert.Equal(
            [new(1, 0), new(1, 1), new(2, 0), new(2, 1)],
            grid.SelectedCells);
        Assert.Equal(1, grid.CurrentColumnIndex);
    }

    [Fact]
    public void TheArrowsMoveBetweenCells()
    {
        var (form, grid) = CreateGrid(DataGridViewSelectionUnit.Cell);
        form.FocusElement(grid);
        grid.SelectedIndex = 0;

        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.PressKey(form, Key.Down);

        Assert.Equal([new DataGridViewCellIndex(1, 1)], grid.SelectedCells);

        HeadlessInput.PressKey(form, Key.End);
        Assert.Equal(2, grid.CurrentColumnIndex);

        HeadlessInput.PressKey(form, Key.End, KeyModifiers.Control);
        Assert.Equal(5, grid.SelectedIndex);
        Assert.Equal(2, grid.CurrentColumnIndex);
    }

    [Fact]
    public void SelectingCellsTheRowsAreNotSelectedButTheirCellsAre()
    {
        var (_, grid) = CreateGrid(DataGridViewSelectionUnit.Cell);

        grid.SetCellSelected(3, 2, true);

        Assert.False(grid.IsRowSelected(3));
        Assert.Equal([3], grid.SelectedIndices);
        Assert.Throws<InvalidOperationException>(() => grid.SetRowSelected(3, true));
    }

    // ===== columns =====

    [Fact]
    public void SelectingColumnsAHeaderClickSelectsInsteadOfSorting()
    {
        var (form, grid) = CreateGrid(DataGridViewSelectionUnit.FullColumn);

        Click(form, HeaderPoint(grid, 1));
        Click(form, HeaderPoint(grid, 2), KeyModifiers.Control);

        Assert.Equal(-1, grid.SortColumnIndex);
        Assert.Equal([grid.Columns[1], grid.Columns[2]], grid.SelectedColumns);
        Assert.True(grid.IsCellSelected(4, 2));
        Assert.False(grid.IsCellSelected(4, 0));
    }

    [Fact]
    public void UpAndDownASelectedColumnKeepTheSelection()
    {
        var (form, grid) = CreateGrid(DataGridViewSelectionUnit.FullColumn);
        form.FocusElement(grid);

        Click(form, CellPoint(grid, 0, 1));
        HeadlessInput.PressKey(form, Key.Down);

        Assert.Equal([grid.Columns[1]], grid.SelectedColumns);
        Assert.Equal(1, grid.SelectedIndex);
    }

    // ===== switching and copying =====

    [Fact]
    public void ChangingTheUnitStartsOverFromTheLead()
    {
        var (_, grid) = CreateGrid();
        grid.SetRowSelected(1, true);
        grid.SetRowSelected(2, true);

        grid.SelectionUnit = DataGridViewSelectionUnit.Cell;

        Assert.Equal([new DataGridViewCellIndex(2, 0)], grid.SelectedCells);
    }

    [Fact]
    public void TurningToSingleKeepsOnlyTheLead()
    {
        var (_, grid) = CreateGrid();
        grid.SelectAll();
        grid.SelectedIndex = 4;
        grid.SetRowSelected(1, true);

        grid.SelectionMode = SelectionMode.Single;

        Assert.Equal([1], grid.SelectedIndices);
    }

    [Fact]
    public void CtrlCCopiesTheSelectionAsATable()
    {
        IClipboard saved = Clipboard.Current;
        var clipboard = new FakeClipboard();
        Clipboard.Current = clipboard;

        try
        {
            var (form, grid) = CreateGrid(DataGridViewSelectionUnit.Cell);
            form.FocusElement(grid);

            grid.SetCellSelected(0, 0, true);
            grid.SetCellSelected(1, 2, true);
            grid.CopyHeaders = true;

            HeadlessInput.PressKey(form, Key.C, KeyModifiers.Control);

            // the columns with something selected; a cell between that isn't, empty
            Assert.Equal(
                string.Join(Environment.NewLine, "Name\tCity", "P0\t", "\tC1"),
                clipboard.Text);
        }
        finally
        {
            Clipboard.Current = saved;
        }
    }

    [Fact]
    public void ScreenReadersSeeTheSelectedCells()
    {
        var (_, grid) = CreateGrid(DataGridViewSelectionUnit.Cell);
        grid.SetCellSelected(2, 1, true);

        AccessibilityPeer gridPeer = grid.GetAccessibilityPeer()!;
        AccessibilityPeer row = gridPeer.Children[3];   // the header row comes first
        AccessibilityPeer cell = row.Children[1];

        Assert.True(gridPeer.States.HasFlag(AccessibilityStates.MultiSelectable));
        Assert.True(cell.States.HasFlag(AccessibilityStates.Selected));
        Assert.False(row.States.HasFlag(AccessibilityStates.Selected));
    }
}
