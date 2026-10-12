using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary><see cref="WrapPanel.Spacing"/> lies between every two neighbours of a
/// line — the first and the second included — and the measure counts it the same.</summary>
[Collection("Platform")]
public class WrapPanelSpacingTests
{
    private static WrapPanel Show(WrapPanel panel, float width)
    {
        var form = new Form { Size = new Size(width, 300), Content = panel };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return panel;
    }

    private static Border Box() => new() { Size = new Size(50, 20) };

    [Fact]
    public void EveryNeighbourKeepsTheGap()
    {
        Border a = Box(), b = Box(), c = Box();
        var panel = new WrapPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Left, Children = { a, b, c } };
        Show(panel, 400);

        Assert.Equal(a.Position.X + 60, b.Position.X, 0.5f);
        Assert.Equal(b.Position.X + 60, c.Position.X, 0.5f);
    }

    [Fact]
    public void ALineEndsWhereTheMeasureSaid()
    {
        Border a = Box(), b = Box(), c = Box();
        var panel = new WrapPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Left, Children = { a, b, c } };
        Show(panel, 400);

        // three boxes and two gaps, nothing after the last
        Assert.Equal(170, panel.DesiredSize.Width, 0.5f);
        Assert.Equal(c.Position.X + 50, panel.DesiredSize.Width, 0.5f);
    }

    [Fact]
    public void AWrappedLineStartsWithoutAGap()
    {
        Border a = Box(), b = Box(), c = Box();

        // room for two boxes and their gap, not for a third
        var panel = new WrapPanel { Spacing = 10, LineSpacing = 4, Size = new Size(120, float.NaN), Children = { a, b, c } };
        Show(panel, 400);

        Assert.Equal(60, b.Position.X - a.Position.X, 0.5f);
        Assert.Equal(a.Position.X, c.Position.X, 0.5f);
        Assert.Equal(a.Position.Y + 24, c.Position.Y, 0.5f);
    }
}
