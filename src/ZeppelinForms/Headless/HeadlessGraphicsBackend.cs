using ZeppelinForms.Drawing;

namespace ZeppelinForms.Headless;

/// <summary>The backend that draws nothing: its surfaces hand out a
/// <see cref="HeadlessGraphics"/>, and all the drawing code still honestly runs. For
/// tests of a platform's frame path, and the smallest example of a backend.</summary>
public sealed class HeadlessGraphicsBackend : IGraphicsBackend
{
    public static HeadlessGraphicsBackend Instance { get; } = new();

    private HeadlessGraphicsBackend() { }

    public string Name => "Headless";

    public void Register()
    {
        HeadlessTextMeasurer.Register();
        HeadlessImageDecoder.Register();
        HeadlessElementRenderer.Register();
    }

    public IRenderSurface? CreatePixelSurface(PixelBuffer buffer) => new Surface();

    /// <summary>There is no GL here: a platform asking falls back to its buffer.</summary>
    public IGlSurface? CreateGlSurface(GlTarget target) => null;

    private sealed class Surface : IRenderSurface
    {
        /// <summary>A new one each frame: a frame starts with no saves and no transform,
        /// whatever the last one left.</summary>
        public Graphics BeginFrame() => new HeadlessGraphics();

        public void EndFrame() { }

        public void Dispose() { }
    }
}