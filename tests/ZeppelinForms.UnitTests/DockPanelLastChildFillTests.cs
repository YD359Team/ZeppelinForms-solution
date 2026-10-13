using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary><see cref="DockPanel.LastChildFill"/>: the last child docked to no edge
/// fills the rest of the room, as if it had <see cref="Dock.Fill"/>.</summary>
[Collection("Platform")]
public class DockPanelLastChildFillTests
{
    private const float Width = 400;
    private const float Height = 300;

    private static DockPanel Show(DockPanel panel)
    {
        var form = new Form { Size = new Size(Width, Height), Content = panel };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return panel;
    }

    private static Label Bar(string text, Dock dock) =>
        new() { Text = text, Docking = dock, Size = new Size(float.NaN, 30) };

    [Fact]
    public void TheLastChildFillsTheRest()
    {
        var content = new TextBox();
        Show(new DockPanel { Children = { Bar("tools", Dock.Top), Bar("status", Dock.Bottom), content } });

        // a text box keeps its natural size by default; as the last child it fills
        Assert.Equal(Width, content.ActualSize.Width, 0.5f);
        Assert.Equal(Height - 60, content.ActualSize.Height, 0.5f);
        Assert.Equal(30, content.Position.Y, 0.5f);
    }

    [Fact]
    public void ItCanBeTurnedOff()
    {
        var content = new TextBox();
        Show(new DockPanel { LastChildFill = false, Children = { Bar("tools", Dock.Top), content } });

        Assert.True(content.ActualSize.Height < Height - 30, $"height {content.ActualSize.Height}");
    }

    [Fact]
    public void ASideSetExplicitlyIsKept()
    {
        // a status bar added after the content stays a bar, unlike in WPF
        var content = new TextBox { Docking = Dock.Fill };
        Label status = Bar("status", Dock.Bottom);

        Show(new DockPanel { Children = { content, status } });

        Assert.Equal(30, status.ActualSize.Height, 0.5f);
        Assert.Equal(Height - 30, status.Position.Y, 0.5f);
        Assert.Equal(Height - 30, content.ActualSize.Height, 0.5f);
    }

    [Fact]
    public void AnExplicitSizeBeatsTheFill()
    {
        var content = new Button { Text = "OK", Size = new Size(80, 40) };
        Show(new DockPanel { Children = { Bar("tools", Dock.Top), content } });

        Assert.Equal(80, content.ActualSize.Width, 0.5f);
        Assert.Equal(40, content.ActualSize.Height, 0.5f);
    }

    [Fact]
    public void OnlyTheLastOneFills()
    {
        var first = new TextBox();
        var last = new TextBox();

        Show(new DockPanel { Children = { first, last } });

        Assert.True(first.ActualSize.Height < Height, $"the first fills: {first.ActualSize.Height}");
        Assert.Equal(Height, last.ActualSize.Height, 0.5f);
    }

    [Fact]
    public void AHiddenLastChildPassesItOn()
    {
        var content = new TextBox();
        var hidden = new TextBox { IsVisible = false };

        Show(new DockPanel { Children = { Bar("tools", Dock.Top), content, hidden } });

        Assert.Equal(Height - 30, content.ActualSize.Height, 0.5f);
    }

    [Fact]
    public void TurningItOffLaysOutAgain()
    {
        var content = new TextBox();
        DockPanel panel = Show(new DockPanel { Children = { content } });

        Assert.Equal(Height, content.ActualSize.Height, 0.5f);

        panel.LastChildFill = false;
        panel.FindForm()!.UpdateLayout();

        Assert.True(content.ActualSize.Height < Height, $"height {content.ActualSize.Height}");
    }
}