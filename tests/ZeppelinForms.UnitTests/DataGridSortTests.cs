using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class DataGridSortTests
{
    private sealed record Person(string Name, int Age);

    private static (Form Form, DataGridView Grid) CreateGrid()
    {
        var platform = new HeadlessPlatform();

        var grid = new DataGridView
        {
            RowHeight = 26f,
            HeaderHeight = 30f,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Columns =
            {
                new DataGridViewColumn
                {
                    Header = "Имя",
                    Width = GridLength.Fixed(120),
                    Value = item => ((Person)item).Name,
                },
                new DataGridViewColumn
                {
                    Header = "Возраст",
                    Width = GridLength.Fixed(80),
                    Value = item => ((Person)item).Age,
                },
            },
        };

        grid.Items.Add(new Person("Вера", 41));
        grid.Items.Add(new Person("Анна", 30));
        grid.Items.Add(new Person("Борис", 35));

        var form = new Form { Size = new Size(400, 300), Content = grid };
        platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, grid);
    }

    private static string NameAt(DataGridView grid, int row) => ((Person)grid.RowItem(row)).Name;

    [Fact]
    public void SortingReordersViewButNotSource()
    {
        var (_, grid) = CreateGrid();

        grid.SortBy(0);

        Assert.Equal("Анна", NameAt(grid, 0));
        Assert.Equal("Вера", NameAt(grid, 2));

        // the collection itself is untouched: sorting is a way of looking at the data
        Assert.Equal("Вера", ((Person)grid.Items[0]).Name);
    }

    [Fact]
    public void RepeatedSortFlipsDirection()
    {
        var (_, grid) = CreateGrid();

        grid.SortBy(1);
        Assert.Equal("Анна", NameAt(grid, 0));

        grid.SortBy(1);

        Assert.True(grid.SortDescending);
        Assert.Equal("Вера", NameAt(grid, 0));
    }

    [Fact]
    public void ClearSortRestoresSourceOrder()
    {
        var (_, grid) = CreateGrid();

        grid.SortBy(0);
        grid.ClearSort();

        Assert.Equal("Вера", NameAt(grid, 0));
        Assert.Equal(-1, grid.SortColumnIndex);
    }

    [Fact]
    public void SelectionFollowsItemThroughSort()
    {
        var (_, grid) = CreateGrid();

        grid.SelectedIndex = 0;                       // the first row in source order
        object? selected = grid.SelectedItem;

        grid.SortBy(0);

        // the selection holds on to the data row, not to its place on screen
        Assert.Same(selected, grid.SelectedItem);
        Assert.Equal(2, grid.SelectedIndex);
    }

    [Fact]
    public void HeaderClickSorts()
    {
        var (form, grid) = CreateGrid();

        // the header is 30 high — click into the first column
        HeadlessInput.Click(form, 40, 15);

        Assert.Equal(0, grid.SortColumnIndex);
        Assert.Equal("Анна", NameAt(grid, 0));

        // and a click on a data row still selects
        HeadlessInput.Click(form, 40, 45);
        Assert.Equal(0, grid.SelectedIndex);
    }

    [Fact]
    public void AddedItemTakesItsPlaceInSortedView()
    {
        var (_, grid) = CreateGrid();

        grid.SortBy(0);
        grid.Items.Add(new Person("Артём", 28));

        // the order is rebuilt: the new row took its place
        Assert.Equal("Анна", NameAt(grid, 0));
        Assert.Equal("Артём", NameAt(grid, 1));
    }

    [Fact]
    public void DraggingEdgeResizesColumn()
    {
        var (form, grid) = CreateGrid();

        // the first column's boundary is at 120 from the left edge
        form.OnPointerDown(new Point(120, 15));
        form.OnPointerMove(new Point(170, 15));
        form.OnPointerUp(new Point(170, 15));
        form.UpdateLayout();

        Assert.Equal(170f, grid.Columns[0].Width.Value, 1f);

        // dragging a boundary doesn't count as a click on the header
        Assert.Equal(-1, grid.SortColumnIndex);
    }

    [Fact]
    public void ColumnCannotBeDraggedBelowMinimum()
    {
        var (form, grid) = CreateGrid();

        form.OnPointerDown(new Point(120, 15));
        form.OnPointerMove(new Point(-200, 15));
        form.OnPointerUp(new Point(-200, 15));
        form.UpdateLayout();

        Assert.Equal(grid.MinColumnWidth, grid.Columns[0].Width.Value, 1f);
    }
}