using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class GridAndDragTouchTests
{
    private static Form Show(UIElement content)
    {
        var platform = new HeadlessPlatform();

        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.VerticalAlignment = VerticalAlignment.Stretch;

        var form = new Form { Size = new Size(400, 300), Content = content };
        platform.CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    /// <summary>A slow finger drag: the finger freezes before the release,
    /// so there is no inertia and exact numbers can be checked.</summary>
    private static void SlowDrag(Form form, Point from, Point to, int steps = 10)
    {
        HeadlessInput.TouchDown(form, 0, from.X, from.Y, 0);

        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;

            HeadlessInput.TouchMove(
                form, 0,
                from.X + ((to.X - from.X) * t),
                from.Y + ((to.Y - from.Y) * t),
                i * 16);
        }

        HeadlessInput.TouchUp(form, 0, to.X, to.Y, steps * 16 + 200);
    }

    private static DataGridView CreateGrid(int rows = 60) => new()
    {
        RowHeight = 26f,
        Columns =
        {
            new DataGridViewColumn { Header = "№", Value = item => item },
        },
    };

    [Fact]
    public void GridScrollsWithTouch()
    {
        DataGridView grid = CreateGrid();

        for (int i = 0; i < 60; i++)
            grid.Items.Add(i);

        Form form = Show(grid);

        // the row under the top of the body before scrolling is row zero
        HeadlessInput.Tap(form, 100, 40);
        Assert.Equal(0, grid.SelectedIndex);

        // 260 / 26 = ten rows up
        SlowDrag(form, new Point(100, 280), new Point(100, 20));
        form.UpdateLayout();

        HeadlessInput.Tap(form, 100, 40);

        // the same point on screen — but a different data row now
        Assert.InRange(grid.SelectedIndex, 9, 11);
    }

    [Fact]
    public void GridIgnoresMouseDrag()
    {
        DataGridView grid = CreateGrid();

        for (int i = 0; i < 60; i++)
            grid.Items.Add(i);

        Form form = Show(grid);

        form.OnPointerDown(new Point(100, 280));

        for (int i = 1; i <= 10; i++)
            form.OnPointerMove(new Point(100, 280 - i * 26));

        form.OnPointerUp(new Point(100, 20));
        form.UpdateLayout();

        HeadlessInput.Tap(form, 100, 40);

        // the mouse doesn't scroll the grid: the row stayed the same
        Assert.Equal(0, grid.SelectedIndex);
    }

    [Fact]
    public void TouchDragDoesNotStartDragListReorder()
    {
        var list = new DragList();

        for (int i = 0; i < 20; i++)
            list.Items.Add($"строка {i}");

        Form form = Show(list);

        object first = list.Items[0];

        // without a hold a finger movement is scrolling, not moving a row
        SlowDrag(form, new Point(50, 200), new Point(50, 40));
        form.UpdateLayout();

        Assert.False(list.IsDragging);
        Assert.Same(first, list.Items[0]);
    }

    [Fact]
    public void MouseDragStillReorders()
    {
        var list = new DragList();

        for (int i = 0; i < 20; i++)
            list.Items.Add($"строка {i}");

        Form form = Show(list);

        form.OnPointerDown(new Point(50, 10));
        form.OnPointerMove(new Point(50, 70));
        form.OnPointerUp(new Point(50, 70));

        // with the mouse everything is as before: the movement threshold, no hold
        Assert.False(list.IsDragging);
        Assert.Equal(20, list.Items.Count);
    }
}