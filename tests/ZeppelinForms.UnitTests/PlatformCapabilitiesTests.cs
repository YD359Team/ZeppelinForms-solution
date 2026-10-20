using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.18.0: <see cref="IPlatform.Capabilities"/>, and a window that can't place
/// itself — Wayland's — refusing <see cref="Form.Position"/> and centering.</summary>
[Collection("Platform")]
public class PlatformCapabilitiesTests
{
    private static HeadlessWindow Open(Form form, bool positioning)
    {
        var window = (HeadlessWindow)new HeadlessPlatform().CreateWindow(form);
        window.SupportsPositioning = positioning;
        form.UpdateLayout();
        return window;
    }

    [Fact]
    public void TheHeadlessPlatformPlacesWindows()
    {
        IPlatform platform = new HeadlessPlatform();

        Assert.True(platform.Capabilities.HasFlag(PlatformCapabilities.WindowPositioning));
    }

    [Fact]
    public void APlatformThatSaysNothingPromisesNothing()
    {
        IPlatform platform = new SilentPlatform();

        Assert.Equal(PlatformCapabilities.None, platform.Capabilities);
    }

    [Fact]
    public void APositionIsRefusedAndTheFormStaysWhereItWas()
    {
        var form = new Form();
        Open(form, positioning: false);

        Point before = form.Position;
        int moves = 0;
        form.LocationChanged += (_, _) => moves++;

        Assert.Throws<PlatformNotSupportedException>(() => form.Position = new Point(100, 100));

        Assert.Equal(before, form.Position);
        Assert.Equal(0, moves);
    }

    [Fact]
    public void CenteringAnOpenWindowIsRefused()
    {
        var form = new Form();
        HeadlessWindow window = Open(form, positioning: false);

        Assert.Throws<PlatformNotSupportedException>(form.CenterToScreen);
        Assert.Equal(0, window.CenterOnScreenCount);
    }

    [Fact]
    public void TheSizeIsStillTheApplicationsToSet()
    {
        var form = new Form();
        Open(form, positioning: false);

        form.Size = new Size(500, 400);

        Assert.Equal(new Size(500, 400), form.Size);
    }

    [Fact]
    public void BeforeTheWindowIsOpenThePositionIsJustKept()
    {
        // no window to ask yet: the platform decides when it makes one
        var form = new Form { Position = new Point(10, 20) };

        Assert.Equal(new Point(10, 20), form.Position);
    }

    private sealed class SilentPlatform : IPlatform
    {
        public IPlatformWindow CreateWindow(Form form) => throw new NotSupportedException();
        public void Start() { }
        public void Exit() { }
    }
}