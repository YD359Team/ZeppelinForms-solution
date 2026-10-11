using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Charts;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Controls.Map;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>The alignment a control starts with, by what it is (see UnitControl), and
/// an explicit Size beating Stretch.</summary>
[Collection("Platform")]
public class AlignmentDefaultsTests
{
    private static Form Show(UIElement content, float width = 800, float height = 600)
    {
        var form = new Form { Size = new Size(width, height), Content = content };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    private static DataGridView Grid(int rows = 100)
    {
        var grid = new DataGridView();
        grid.Columns.Add(new DataGridViewColumn { Header = "N", Value = item => item });

        for (int i = 0; i < rows; i++) grid.Items.Add(i);

        return grid;
    }

    public static TheoryData<Type> Views => new() { typeof(DataGridView), typeof(MapControl), typeof(LineChart), typeof(BarChart), typeof(PieChart) };

    public static TheoryData<Type> InPlace => new() { typeof(Button), typeof(TextBox), typeof(Label), typeof(CheckBox) };

    [Theory]
    [MemberData(nameof(Views))]
    public void AViewOntoContentFillsItsSpace(Type type)
    {
        var control = (UIElement)Activator.CreateInstance(type)!;

        Assert.Equal(HorizontalAlignment.Stretch, control.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Stretch, control.VerticalAlignment);
    }

    [Theory]
    [MemberData(nameof(InPlace))]
    public void AnInPlaceControlKeepsItsNaturalSize(Type type)
    {
        var control = (UIElement)Activator.CreateInstance(type)!;

        Assert.Equal(HorizontalAlignment.Center, control.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, control.VerticalAlignment);
    }

    [Fact]
    public void AGridLastInADockPanelFillsTheRestWithoutDockFill()
    {
        // ZF Process Monitor: the grid needed Dock.Fill, or it stood as a strip of
        // ten rows in the middle
        var top = new Label { Text = "top", Docking = Dock.Top };
        var bottom = new Label { Text = "bottom", Docking = Dock.Bottom };
        var grid = Grid();

        Show(new DockPanel { Children = { top, bottom, grid } }, 1024, 768);

        Assert.Equal(top.ActualSize.Height, grid.Position.Y, 0.5f);
        Assert.Equal(768 - top.ActualSize.Height - bottom.ActualSize.Height, grid.ActualSize.Height, 0.5f);
        Assert.Equal(1024, grid.ActualSize.Width, 0.5f);
    }

    [Theory]
    [InlineData(SplitViewDisplayMode.Overlay)]
    [InlineData(SplitViewDisplayMode.CompactInline)]
    public void AGridInASplitViewFillsTheContentArea(SplitViewDisplayMode mode)
    {
        var grid = Grid();
        var view = new SplitView { DisplayMode = mode, Pane = new StackPanel(), Content = grid };

        Show(view);

        Assert.Equal(0, grid.Position.Y, 0.5f);
        Assert.Equal(600, grid.ActualSize.Height, 0.5f);
        Assert.Equal(800 - view.CompactPaneLength * (mode == SplitViewDisplayMode.CompactInline ? 1 : 0), grid.ActualSize.Width, 0.5f);
    }

    [Fact]
    public void AnEmptyGridFillsToo()
    {
        // before its rows arrive — a grid filled in Form.Shown
        var grid = Grid(rows: 0);

        Show(new Border { Child = grid });

        Assert.Equal(600, grid.ActualSize.Height, 0.5f);
    }

    [Fact]
    public void AnExplicitSizeBeatsStretch()
    {
        // stretched along by default, but Size says 100×10
        var bar = new ProgressBar { Size = new Size(100, 10) };

        Show(new Border { Child = bar }, 400, 300);

        Assert.Equal(100, bar.ActualSize.Width, 0.5f);
        Assert.Equal(10, bar.ActualSize.Height, 0.5f);

        // and stands in the middle of the slot
        Assert.Equal(150, bar.Position.X, 0.5f);
        Assert.Equal(145, bar.Position.Y, 0.5f);
    }

    [Fact]
    public void AnExplicitSizeBeatsDockFill()
    {
        var chart = new LineChart { Size = new Size(300, 200), Docking = Dock.Fill };

        Show(new DockPanel { Children = { chart } }, 800, 600);

        Assert.Equal(300, chart.ActualSize.Width, 0.5f);
        Assert.Equal(200, chart.ActualSize.Height, 0.5f);
    }

    [Fact]
    public void AChartWithASizeKeepsIt()
    {
        var chart = new BarChart { Size = new Size(320, 240) };

        Show(new Border { Child = chart });

        Assert.Equal(320, chart.ActualSize.Width, 0.5f);
        Assert.Equal(240, chart.ActualSize.Height, 0.5f);
    }
}