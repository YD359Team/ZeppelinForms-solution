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

        // сама коллекция не тронута: сортировка — способ смотреть на данные
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

        grid.SelectedIndex = 0;                       // Вера
        object? selected = grid.SelectedItem;

        grid.SortBy(0);

        // выделение держится за строку данных, а не за её место на экране
        Assert.Same(selected, grid.SelectedItem);
        Assert.Equal(2, grid.SelectedIndex);
    }

    [Fact]
    public void HeaderClickSorts()
    {
        var (form, grid) = CreateGrid();

        // заголовок высотой 30 — щёлкаем в первый столбец
        HeadlessInput.Click(form, 40, 15);

        Assert.Equal(0, grid.SortColumnIndex);
        Assert.Equal("Анна", NameAt(grid, 0));

        // и по строке данных щелчок по-прежнему выделяет
        HeadlessInput.Click(form, 40, 45);
        Assert.Equal(0, grid.SelectedIndex);
    }

    [Fact]
    public void AddedItemTakesItsPlaceInSortedView()
    {
        var (_, grid) = CreateGrid();

        grid.SortBy(0);
        grid.Items.Add(new Person("Артём", 28));

        // порядок пересобран: новая строка встала на своё место
        Assert.Equal("Анна", NameAt(grid, 0));
        Assert.Equal("Артём", NameAt(grid, 1));
    }

    [Fact]
    public void DraggingEdgeResizesColumn()
    {
        var (form, grid) = CreateGrid();

        // граница первого столбца — на 120 от левого края
        form.OnPointerDown(new Point(120, 15));
        form.OnPointerMove(new Point(170, 15));
        form.OnPointerUp(new Point(170, 15));
        form.UpdateLayout();

        Assert.Equal(170f, grid.Columns[0].Width.Value, 1f);

        // тяга границы не считается щелчком по заголовку
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