using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.17.0: <see cref="UIElement.Width"/> and <see cref="UIElement.Height"/>
/// set one axis of the explicit size, and a child docked to an edge of a
/// <see cref="DockPanel"/> spans it along that edge even over an explicit size.</summary>
[Collection("Platform")]
public class DockSpanTests
{
    private const float FormWidth = 800;
    private const float FormHeight = 600;

    private static void Show(UIElement content)
    {
        var form = new Form { Size = new Size(FormWidth, FormHeight), Content = content };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();
    }

    [Fact]
    public void AHeightAloneLeavesTheWidthToTheDock()
    {
        var box = new TextBox { Docking = Dock.Top, Height = 30 };

        Show(new DockPanel { Children = { box, new Panel() } });

        Assert.True(float.IsNaN(box.Width));
        Assert.Equal(Size.FromHeight(30), box.Size);
        Assert.Equal(FormWidth, box.ActualSize.Width, 0.5f);
        Assert.Equal(30, box.ActualSize.Height, 0.5f);
    }

    [Theory]
    [InlineData(Dock.Top)]
    [InlineData(Dock.Bottom)]
    public void ADockToTheTopOrBottomBeatsAnExplicitWidth(Dock dock)
    {
        var box = new TextBox { Docking = dock, Size = new Size(200, 30) };

        Show(new DockPanel { Children = { box, new Panel() } });

        Assert.Equal(FormWidth, box.ActualSize.Width, 0.5f);
        Assert.Equal(30, box.ActualSize.Height, 0.5f);
        Assert.Equal(dock == Dock.Top ? 0 : FormHeight - 30, box.Position.Y, 0.5f);
    }

    [Theory]
    [InlineData(Dock.Left)]
    [InlineData(Dock.Right)]
    public void ADockToTheLeftOrRightBeatsAnExplicitHeight(Dock dock)
    {
        var side = new Panel { Docking = dock, Size = new Size(150, 100) };

        Show(new DockPanel { Children = { side, new Panel() } });

        Assert.Equal(150, side.ActualSize.Width, 0.5f);
        Assert.Equal(FormHeight, side.ActualSize.Height, 0.5f);
    }

    [Fact]
    public void AnExplicitSizeStillBeatsDockFill()
    {
        var chart = new Panel { Docking = Dock.Fill, Size = new Size(300, 200) };

        Show(new DockPanel { Children = { chart } });

        Assert.Equal(300, chart.ActualSize.Width, 0.5f);
        Assert.Equal(200, chart.ActualSize.Height, 0.5f);
    }

    [Fact]
    public void ADockOutsideADockPanelMeansNothing()
    {
        var box = new TextBox { Docking = Dock.Top, Size = new Size(200, 30) };

        Show(new StackPanel { Children = { box } });

        Assert.Equal(200, box.ActualSize.Width, 0.5f);
    }

    [Fact]
    public void NaNResetsOneAxisOnly()
    {
        var box = new TextBox { Size = new Size(200, 30) };

        box.Width = float.NaN;

        Assert.Equal(Size.FromHeight(30), box.Size);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(float.PositiveInfinity)]
    public void AnInvalidLengthThrows(float length)
    {
        var box = new TextBox();

        Assert.Equal(nameof(UIElement.Width), Assert.Throws<ArgumentOutOfRangeException>(() => box.Width = length).ParamName);
        Assert.Equal(nameof(UIElement.Height), Assert.Throws<ArgumentOutOfRangeException>(() => box.Height = length).ParamName);
    }
    [Fact]
    public void AnAlignmentChosenAlongTheEdgeIsKept()
    {
        // a message box's buttons: docked to the bottom, at the right
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Docking = Dock.Bottom,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { new Button { Size = new Size(96, 32) }, new Button { Size = new Size(96, 32) } },
        };

        Show(new DockPanel { Children = { buttons, new Panel() } });

        Assert.Equal(200, buttons.ActualSize.Width, 0.5f);
        Assert.Equal(FormWidth - 200, buttons.Position.X, 0.5f);
    }

    [Fact]
    public void AStackWithoutFlexibleChildrenAsksForWhatTheyNeed()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new Button { Size = new Size(96, 32) }, new Button { Size = new Size(96, 32) } },
        };

        stack.Measure(new Size(480, 100));

        Assert.Equal(200, stack.DesiredSize.Width, 0.5f);
    }
}
