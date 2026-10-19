using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.17.0: <see cref="Form.MinimumSize"/> and <see cref="Form.MaximumSize"/>
/// reach the window, <see cref="Form.TopMost"/> and <see cref="Form.CenterToScreen"/>
/// are new — the TODOs of IDesktopWindow.</summary>
[Collection("Platform")]
public class FormPlacementTests
{
    private static HeadlessWindow Open(Form form)
    {
        var window = (HeadlessWindow)new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();
        return window;
    }

    [Fact]
    public void TheLimitsReachAnOpenWindow()
    {
        var form = new Form();
        HeadlessWindow window = Open(form);

        form.MinimumSize = new Size(300, 200);
        form.MaximumSize = new Size(1000, float.NaN);

        Assert.Equal(new Size(300, 200), window.MinimumSizeLimit);
        Assert.Equal(new Size(1000, float.NaN), window.MaximumSizeLimit);
    }

    [Fact]
    public void AWindowOpensWithTheLimitsSetBefore()
    {
        var form = new Form { MinimumSize = new Size(300, 200), TopMost = true };

        HeadlessWindow window = Open(form);

        Assert.Equal(new Size(300, 200), window.MinimumSizeLimit);
        Assert.True(window.IsTopMost);
    }

    [Fact]
    public void AMinimumAboveTheMaximumRaisesIt()
    {
        var form = new Form { MaximumSize = new Size(400, 300) };

        form.MinimumSize = new Size(500, 100);

        // the width moves up to the new minimum; the height had no conflict
        Assert.Equal(new Size(500, 300), form.MaximumSize);
        Assert.Equal(new Size(500, 100), form.MinimumSize);
    }

    [Fact]
    public void AMaximumBelowTheMinimumLowersIt()
    {
        var form = new Form { MinimumSize = new Size(400, 300) };

        form.MaximumSize = new Size(float.NaN, 200);

        Assert.Equal(new Size(400, 200), form.MinimumSize);
        Assert.Equal(new Size(float.NaN, 200), form.MaximumSize);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(float.PositiveInfinity)]
    public void AnInvalidLimitThrows(float length)
    {
        var form = new Form();

        Assert.Equal(nameof(Form.MinimumSize),
            Assert.Throws<ArgumentOutOfRangeException>(() => form.MinimumSize = new Size(length, 10)).ParamName);
        Assert.Equal(nameof(Form.MaximumSize),
            Assert.Throws<ArgumentOutOfRangeException>(() => form.MaximumSize = new Size(10, length)).ParamName);
    }

    [Fact]
    public void TheOpeningSizeKeepsWithinTheLimits()
    {
        var form = new Form
        {
            Size = new Size(100, 900),
            MinimumSize = new Size(300, float.NaN),
            MaximumSize = new Size(float.NaN, 600),
        };

        Assert.Equal(new Size(300, 600), form.ClampToSizeLimits(form.Size));
        Assert.Equal(new Size(500, 500), form.ClampToSizeLimits(new Size(500, 500)));
    }

    [Fact]
    public void TopMostReachesAnOpenWindow()
    {
        var form = new Form();
        HeadlessWindow window = Open(form);

        form.TopMost = true;
        Assert.True(window.IsTopMost);

        form.TopMost = false;
        Assert.False(window.IsTopMost);
    }

    [Fact]
    public void CenterToScreenCentersAnOpenWindow()
    {
        var form = new Form();
        HeadlessWindow window = Open(form);

        form.CenterToScreen();

        Assert.Equal(1, window.CenterOnScreenCount);
        Assert.Equal(WindowStartupLocation.Default, form.WindowStartupLocation);
    }

    [Fact]
    public void SettingTheSizeOfAnOpenFormResizesTheWindow()
    {
        var form = new Form { Size = new Size(400, 300) };
        Open(form);

        int changes = 0;
        form.SizeChanged += (_, _) => changes++;

        form.Size = new Size(640, 480);

        // headless has no frame: the client area is the whole window
        Assert.Equal(new Size(640, 480), form.ClientSize);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void SettingThePositionOfAnOpenFormRaisesLocationChangedOnce()
    {
        var form = new Form();
        Open(form);

        int changes = 0;
        form.LocationChanged += (_, _) => changes++;

        form.Position = new Point(120, 80);

        // the window reported the move back: the same place, no second event
        Assert.Equal(new Point(120, 80), form.Position);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AMoveAndAResizeByTheUserReachTheForm()
    {
        var form = new Form { Size = new Size(400, 300) };
        Open(form);

        int moves = 0, resizes = 0;
        form.LocationChanged += (_, _) => moves++;
        form.SizeChanged += (_, _) => resizes++;

        // as a platform reports a drag of the title bar and of an edge
        form.OnWindowMoved(new Point(10, 20));
        form.OnWindowResized(new Size(500, 350));

        Assert.Equal(new Point(10, 20), form.Position);
        Assert.Equal(new Size(500, 350), form.Size);
        Assert.Equal(1, moves);
        Assert.Equal(1, resizes);
    }

    [Fact]
    public void ASizeFromCodeKeepsWithinTheLimits()
    {
        var form = new Form { Size = new Size(400, 300), MaximumSize = new Size(500, 500) };
        Open(form);

        form.Size = new Size(900, 200);

        Assert.Equal(new Size(500, 200), form.ClientSize);
        Assert.Equal(new Size(500, 200), form.Size);
    }

    [Fact]
    public void CenterToScreenBeforeOpeningOpensCentered()
    {
        var form = new Form();

        form.CenterToScreen();

        Assert.Equal(WindowStartupLocation.CenterScreen, form.WindowStartupLocation);
    }
}