using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>0.16: the grid's horizontal bar appears only when there is something to
/// scroll, and scrolls by the wheel too; rows can be striped and styled per record.</summary>
[Collection("Platform")]
public class DataGridRowsAndWidthTests
{
    private sealed record Process(int Id, string Name, bool Responding);

    private sealed class RecordingGraphics : HeadlessGraphics
    {
        public List<(Rectangle Rect, Color Color)> Fills { get; } = [];
        public List<(string Text, Color Color, Font Font)> Texts { get; } = [];

        public override void FillRectangle(Rectangle rect, Color color) => Fills.Add((rect, color));

        public override void DrawText(string text, Rectangle rect, Color color, Font font,
            HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
            VerticalContentAlignment vAlign = VerticalContentAlignment.Center) =>
            Texts.Add((text, color, font));
    }

    private static DataGridView CreateGrid(float width, int columns = 6, int rows = 100)
    {
        var grid = new DataGridView
        {
            Size = new Size(width, 300),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        // all star columns, as a grid gets them by default
        for (int c = 0; c < columns; c++)
        {
            int column = c;
            grid.Columns.Add(new DataGridViewColumn
            {
                Header = $"C{c}",
                Value = item => $"{((Process)item).Name} {column}",
            });
        }

        for (int i = 0; i < rows; i++)
            grid.Items.Add(new Process(i, $"process{i}", i % 3 != 0));

        var form = new Form { Size = new Size(width + 40, 360), Content = grid };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return grid;
    }

    [Fact]
    public void StarColumnsShowNoHorizontalBarAtAnyWidth()
    {
        // widths that aren't round numbers: the sum of the star shares came out a
        // hair over the room, and a dead bar was shown
        for (float width = 600.1f; width < 900f; width += 7.3f)
        {
            DataGridView grid = CreateGrid(width);

            Assert.False(grid.ScrollState.HorizontalBar, $"A bar at the width of {width}");
        }
    }

    [Fact]
    public void ANarrowGridScrollsSidewaysByTheWheel()
    {
        // six columns of at least 32 px don't fit into 150
        DataGridView grid = CreateGrid(150f);
        Form form = grid.FindForm()!;

        Assert.True(grid.ScrollState.HorizontalBar);

        Point origin = grid.GetAbsolutePosition();
        var middle = new Point(origin.X + 60, origin.Y + 100);

        // a tilt of the wheel, or a touchpad's sideways swipe
        form.OnMouseWheel(middle, 0, -120);
        Assert.True(grid.ScrollState.Offset.X > 0);
        Assert.Equal(0f, grid.ScrollState.Offset.Y);

        // Shift turns the wheel sideways
        float x = grid.ScrollState.Offset.X;

        Keyboard.OnDown(Key.Shift, KeyModifiers.Shift);
        try
        {
            form.OnMouseWheel(middle, 120);
        }
        finally
        {
            Keyboard.Reset();
        }

        Assert.True(grid.ScrollState.Offset.X < x);
        Assert.Equal(0f, grid.ScrollState.Offset.Y);

        // and the wheel alone scrolls down, as before
        form.OnMouseWheel(middle, -120);
        Assert.True(grid.ScrollState.Offset.Y > 0);
    }

    [Fact]
    public void TheHorizontalThumbDrags()
    {
        DataGridView grid = CreateGrid(150f);
        Form form = grid.FindForm()!;

        // the thumb is at the left end of the bar under the rows
        Rectangle body = grid.VisibleBodyBounds;
        var start = new Point(body.X + 4f, body.Bottom + 5f);

        form.OnPointerDown(start);
        form.OnPointerMove(new Point(start.X + 60f, start.Y));
        form.OnPointerUp(new Point(start.X + 60f, start.Y));

        Assert.True(grid.ScrollState.Offset.X > 0);
        Assert.Equal(-1, grid.SelectedIndex);
    }

    [Fact]
    public void EveryOtherRowIsStriped()
    {
        DataGridView grid = CreateGrid(600f, rows: 4);
        grid.ShowAlternateRows = true;
        grid.AlternateRowColor = Colors.Red;

        var g = new RecordingGraphics();
        grid.Draw(g);

        Rectangle body = grid.VisibleBodyBounds;
        Point origin = grid.GetAbsolutePosition();

        // the stripes, by their place: rows 1 and 3
        float[] striped = [.. g.Fills.Where(f => f.Color == Colors.Red).Select(f => f.Rect.Y + origin.Y - body.Y)];

        Assert.Equal(new[] { grid.RowHeight, grid.RowHeight * 3 }, striped);
    }

    [Fact]
    public void ARowTakesTheStyleOfItsRecord()
    {
        DataGridView grid = CreateGrid(600f, rows: 3);

        var hung = new DataGridViewRowStyle
        {
            Background = Colors.Yellow,
            TextColor = Colors.Red,
            Font = Font.Default.Bold(),
        };

        grid.RowStyle = item => ((Process)item).Responding ? null : hung;

        var g = new RecordingGraphics();
        grid.Draw(g);

        // row 0 does not respond: its cells are red and bold on yellow
        Assert.Contains(g.Fills, f => f.Color == Colors.Yellow);
        Assert.All(g.Texts.Where(t => t.Text.StartsWith("process0 ", StringComparison.Ordinal)), t =>
        {
            Assert.Equal(Colors.Red, t.Color);
            Assert.Equal(FontWeight.Bold, t.Font.Weight);
        });

        // the others keep the grid's look
        Assert.All(g.Texts.Where(t => t.Text.StartsWith("process1 ", StringComparison.Ordinal)), t =>
            Assert.Equal(grid.TextColor, t.Color));

        // the selection wins over the row's fill
        grid.SelectedIndex = 0;
        g = new RecordingGraphics();
        grid.Draw(g);

        Assert.DoesNotContain(g.Fills, f => f.Color == Colors.Yellow);
    }
}
