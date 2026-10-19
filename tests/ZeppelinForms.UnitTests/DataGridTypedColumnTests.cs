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

    [Fact]
    public void AColumnReadingOneMemberIsHeadedByIt()
    {
        var column = new DataGridViewColumn<Game, int>(game => game.Year);
        var check = new DataGridViewCheckBoxColumn<Game>(static g => g.Year > 2000);
        var named = new DataGridViewColumn<Game, string>(game => game.Title) { Header = "Название" };

        Assert.Equal("Year", column.Header);
        Assert.Null(check.Header);
        Assert.Equal("Название", named.Header);
    }

    [Fact]
    public void AFailingColumnIsNamedInTheException()
    {
        var grid = new DataGridView
        {
            Columns =
            {
                // the usual mistake: a cast to the wrong record type
                new DataGridViewColumn { Header = "Broken", Value = item => ((Note)item).Text },
            },
            Size = new ZeppelinForms.Drawing.Primitives.Size(300, 200),
        };

        grid.Items.Add(new Game("A", 1998));

        var form = new ZeppelinForms.Forms.Form { Size = new ZeppelinForms.Drawing.Primitives.Size(400, 300), Content = grid };
        new ZeppelinForms.Headless.HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        var exception = Assert.ThrowsAny<InvalidOperationException>(() => grid.Draw(new ZeppelinForms.Headless.HeadlessGraphics()));

        Assert.Contains("\"Broken\"", exception.Message);
        Assert.Contains(nameof(Game), exception.Message);
        Assert.IsType<InvalidCastException>(exception.InnerException);
    }
}
