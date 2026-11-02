using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.20: a grid places a cell where its size says it is — at the start of its
/// track, and in the last track for an index past them.</summary>
[Collection("Platform")]
public class GridPlacementTests
{
    private static UIElement Cell(int row, int column) => new Panel
    {
        Row = row,
        Column = column,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    [Fact]
    public void ACellStartsWhereItsTrackDoes()
    {
        UIElement middle = Cell(1, 1);
        UIElement corner = Cell(2, 2);
        var grid = new Grid { Columns = "50,100,*", Rows = "20,30,*", Children = { Cell(0, 0), middle, corner } };

        var form = new Form { Size = new Size(400, 300), Content = grid };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        Assert.Equal(new Point(50, 20), middle.Position);
        Assert.Equal(new Size(100, 30), middle.ActualSize);
        Assert.Equal(new Point(150, 50), corner.Position);
    }

    [Fact]
    public void ACellPastTheTracksIsInTheLastOne()
    {
        UIElement past = Cell(0, 7);
        var grid = new Grid { Columns = "50,100", Rows = "40", Children = { past } };

        var form = new Form { Size = new Size(400, 300), Content = grid };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        // its width was the last column's, its start past both of them
        Assert.Equal(50f, past.Position.X);
        Assert.Equal(100f, past.ActualSize.Width);
    }
}
