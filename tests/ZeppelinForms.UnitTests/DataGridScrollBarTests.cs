using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>The grid's own scrollbars take the mouse: a press on the track pages,
/// the thumb drags, and neither selects the row behind the bar.</summary>
[Collection("Platform")]
public class DataGridScrollBarTests
{
    private const float BarThickness = 10f;

    private static (Form Form, DataGridView Grid) CreateGrid(int rows = 100)
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
                new DataGridViewColumn { Header = "N", Width = GridLength.Star(1), Value = item => item },
            },
        };

        for (int i = 0; i < rows; i++) grid.Items.Add(i);

        var form = new Form { Size = new Size(400, 300), Content = grid };
        platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, grid);
    }

    /// <summary>How far the rows are scrolled: the first row's band moves up by it.</summary>
    private static float ScrollY(DataGridView grid) => grid.VisibleBodyBounds.Y - grid.RowBounds(0).Y;

    /// <summary>A point on the vertical bar, at a fraction of the body's height.</summary>
    private static Point OnVerticalBar(DataGridView grid, float fraction)
    {
        Rectangle body = grid.VisibleBodyBounds;
        Point origin = grid.GetAbsolutePosition();

        return new Point(
            origin.X + grid.ActualSize.Width - grid.Padding.Right - BarThickness / 2f,
            body.Y + body.Height * fraction);
    }

    private static void Click(Form form, Point at)
    {
        form.OnPointerDown(at);
        form.OnPointerUp(at);
    }

    [Fact]
    public void APressOnTheTrackPagesAndSelectsNothing()
    {
        var (form, grid) = CreateGrid();

        // the thumb is at the top: below it is the track
        Click(form, OnVerticalBar(grid, 0.9f));

        Assert.Equal(grid.VisibleBodyBounds.Height, ScrollY(grid), 0.5f);
        Assert.Equal(-1, grid.SelectedIndex);

        // and back up
        Click(form, OnVerticalBar(grid, 0.02f));

        Assert.Equal(0f, ScrollY(grid), 0.5f);
        Assert.Equal(-1, grid.SelectedIndex);
    }

    [Fact]
    public void TheThumbDragsToTheEnd()
    {
        var (form, grid) = CreateGrid();

        // grab the thumb at the top of the track and pull it past the bottom
        Point grab = OnVerticalBar(grid, 0.01f);

        form.OnPointerDown(grab);
        form.OnPointerMove(new Point(grab.X, grab.Y + 120));

        float halfway = ScrollY(grid);
        Assert.True(halfway > 0);

        // the cursor may leave the bar sideways and below: the capture keeps the drag
        form.OnPointerMove(new Point(grab.X - 200, grab.Y + 1000));
        form.OnPointerUp(new Point(grab.X - 200, grab.Y + 1000));

        float max = 100 * 26f - grid.VisibleBodyBounds.Height;
        Assert.Equal(max, ScrollY(grid), 0.5f);
        Assert.Equal(-1, grid.SelectedIndex);

        // released: moving the mouse no longer scrolls
        form.OnPointerMove(new Point(grab.X, grab.Y));
        Assert.Equal(max, ScrollY(grid), 0.5f);
    }

    [Fact]
    public void RowsStillSelectBesideTheBar()
    {
        var (form, grid) = CreateGrid();

        Rectangle body = grid.VisibleBodyBounds;

        // the second row, just left of the bar
        Click(form, new Point(body.X + body.Width - 2, body.Y + 26 + 13));

        Assert.Equal(1, grid.SelectedIndex);
    }

    [Fact]
    public void WithoutABarTheEdgeIsARow()
    {
        var (form, grid) = CreateGrid(rows: 3);

        Point origin = grid.GetAbsolutePosition();
        Rectangle body = grid.VisibleBodyBounds;

        // three rows fit: no bar, so the right edge belongs to the rows
        Click(form, new Point(origin.X + grid.ActualSize.Width - grid.Padding.Right - 3, body.Y + 13));

        Assert.Equal(0, grid.SelectedIndex);
    }
}