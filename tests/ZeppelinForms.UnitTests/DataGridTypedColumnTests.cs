using System.Globalization;
using Xunit;
using ZeppelinForms.Forms.Controls.DataGrid;

namespace ZeppelinForms.UnitTests;

/// <summary>0.17.0: a typed column — no casts in the delegates, the same column to the grid.</summary>
[Collection("Platform")]
public class DataGridTypedColumnTests
{
    private sealed record Game(string Title, int Year);

    private sealed record Note(string Text);

    private static DataGridView Grid(params DataGridViewColumn[] columns)
    {
        var grid = new DataGridView();
        grid.Columns.AddRange(columns);

        grid.Items.Add(new Game("B", 2004));
        grid.Items.Add(new Game("A", 1998));
        grid.Items.Add(new Game("C", 2011));

        return grid;
    }

    [Fact]
    public void TheTypedFormatTurnsTheValueIntoText()
    {
        DataGridView grid = Grid(new DataGridViewColumn<Game, int>(game => game.Year)
        {
            Format = year => year.ToString("N0", CultureInfo.InvariantCulture),
        });

        Assert.Equal("2,004", grid.CellText(0, 0));
        Assert.Equal(2004, grid.CellValue(0, 0));
    }

    [Fact]
    public void ItSortsByTheTypedValueAndTheTypedComparer()
    {
        DataGridView grid = Grid(new DataGridViewColumn<Game, int>(game => game.Year));

        grid.SortBy(0);
        Assert.Equal(1998, grid.RowItem<Game>(0).Year);

        // newest first, by a comparer of ints
        ((DataGridViewColumn<Game, int>)grid.Columns[0]).Comparer =
            Comparer<int>.Create((left, right) => right.CompareTo(left));

        grid.SortBy(0, descending: false);
        Assert.Equal(2011, grid.RowItem<Game>(0).Year);
    }

    [Fact]
    public void ARecordOfAnotherKindShowsAnEmptyCellAndSortsFirst()
    {
        DataGridView grid = Grid(new DataGridViewColumn<Game, string>(game => game.Title));
        grid.Items.Add(new Note("not a game"));

        Assert.Equal(string.Empty, grid.CellText(3, 0));
        Assert.Null(grid.CellValue(3, 0));

        grid.SortBy(0);

        Assert.IsType<Note>(grid.RowItem(0));
    }

    [Fact]
    public void AFormatSetThroughTheUntypedColumnTakesOver()
    {
        var typed = new DataGridViewColumn<Game, int>(game => game.Year) { Format = year => $"typed {year}" };
        DataGridView grid = Grid(typed);

        DataGridViewColumn untyped = typed;
        untyped.Format = value => $"untyped {value}";

        Assert.Equal("untyped 2004", grid.CellText(0, 0));
    }

    [Fact]
    public void TheTypedSetterWritesThroughTheUntypedOne()
    {
        var games = new Dictionary<Game, int>();

        var column = new DataGridViewColumn<Game, int>(game => game.Year)
        {
            SetValue = (game, year) => games[game] = year,
        };

        var record = new Game("A", 1998);
        ((DataGridViewColumn)column).SetValue!(record, 2000);

        Assert.Equal(2000, games[record]);
    }
}
