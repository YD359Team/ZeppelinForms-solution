using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>The frame every backend draws: <see cref="FormRenderer"/> over the abstract
/// graphics, and the headless backend as the smallest one.</summary>
[Collection("Platform")]
public class FormRendererTests
{
    /// <summary>Records the calls a frame makes, in order.</summary>
    private sealed class Recorder : HeadlessGraphics
    {
        public List<string> Calls { get; } = [];
        public int Depth { get; private set; }
        public int MaxDepth { get; private set; }

        public override void Clear(Color color) => Calls.Add($"clear {color}");

        public override void FillRectangle(Rectangle rect, Color color) => Calls.Add($"fill {rect.Size} {color}");

        public override void Scale(float sx, float sy) => Calls.Add($"scale {sx}");

        public override void Translate(float dx, float dy) => Calls.Add($"translate {dx},{dy}");

        public override void ClipRect(Rectangle bounds) => Calls.Add($"clip {bounds}");

        public override void Save()
        {
            Depth++;
            MaxDepth = Math.Max(MaxDepth, Depth);
        }

        public override void Restore() => Depth--;
    }

    private static Form Show(Color background)
    {
        var form = new Form { Size = new Size(200, 100), Background = background, Content = new StackPanel() };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    [Fact]
    public void ARootFormClearsTheCanvasToItsBackgroundUnderItsScale()
    {
        var red = new Color(255, 0, 0);
        Form form = Show(red);
        var g = new Recorder();

        FormRenderer.Render(form, g, scale: 2f);

        Assert.Equal("scale 2", g.Calls[0]);
        Assert.Contains($"clear {red}", g.Calls);
        Assert.DoesNotContain(g.Calls, c => c.StartsWith("fill", StringComparison.Ordinal) && c.Contains(red.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void AFormOverAnotherPaintsOnlyItsOwnArea()
    {
        var blue = new Color(0, 0, 255);
        Form form = Show(blue);
        var g = new Recorder();

        FormRenderer.Render(form, g, clearBackground: false, origin: new Point(30, 40));

        Assert.Contains("translate 30,40", g.Calls);
        Assert.DoesNotContain(g.Calls, c => c.StartsWith("clear", StringComparison.Ordinal));
        Assert.Contains($"fill {form.ClientSize} {blue}", g.Calls);
    }

    [Fact]
    public void ADirtyAreaClipsTheFrame()
    {
        Form form = Show(Colors.White);
        var g = new Recorder();
        var dirty = new Rectangle(10, 10, 20, 20);

        FormRenderer.Render(form, g, clip: dirty);

        Assert.Contains($"clip {dirty}", g.Calls);
    }

    [Fact]
    public void TheFrameRestoresEverySaveItMakes()
    {
        Form form = Show(Colors.White);
        var g = new Recorder();

        FormRenderer.Render(form, g);

        Assert.Equal(0, g.Depth);
        Assert.True(g.MaxDepth >= 1);
    }

    [Fact]
    public void TheHeadlessBackendDrawsIntoBuffersButNotIntoGl()
    {
        IGraphicsBackend backend = HeadlessGraphicsBackend.Instance;

        using IRenderSurface? surface = backend.CreatePixelSurface(new PixelBuffer(0, 10, 10, 40, PixelFormat.Bgra8888));

        Assert.NotNull(surface);
        Assert.Null(backend.CreateGlSurface(new GlTarget { Width = 10, Height = 10 }));

        // each frame starts with a fresh graphics, whatever the last one left
        Graphics first = surface!.BeginFrame();
        surface.EndFrame();
        Graphics second = surface.BeginFrame();
        surface.EndFrame();

        Assert.NotSame(first, second);
    }
}