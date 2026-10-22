using ZeppelinForms.Drawing;
using ZeppelinForms.Gles.Rendering;

namespace ZeppelinForms.Gles;

/// <summary>A surface drawing into the framebuffer of a platform's GL context: the program,
/// the atlases and the pictures' textures live as long as the window, a resize changes
/// only the size.</summary>
internal sealed class GlesSurface : IGlSurface
{
    private readonly GlesRenderer _renderer;
    private readonly GlesGraphics _graphics;
    private readonly uint _framebuffer;

    private int _width;
    private int _height;
    private bool _inFrame;

    public GlesSurface(GlesRenderer renderer, GlTarget target)
    {
        _renderer = renderer;
        _graphics = new GlesGraphics(renderer);
        _framebuffer = target.Framebuffer;
        _width = target.Width;
        _height = target.Height;
    }

    internal GlesRenderer Renderer => _renderer;

    public void Resize(int width, int height)
    {
        _width = Math.Max(0, width);
        _height = Math.Max(0, height);
    }

    public Graphics BeginFrame()
    {
        if (_width <= 0 || _height <= 0)
            throw new InvalidOperationException("The GL surface has no size yet.");

        var target = new RenderTarget(_framebuffer, _width, _height, 0, 0);

        _renderer.BeginFrame(target);
        _graphics.Begin(target);
        _inFrame = true;

        return _graphics;
    }

    public void EndFrame()
    {
        if (!_inFrame) return;

        _inFrame = false;

        // a frame broken off by an exception left its layers open: they go back now
        _graphics.End();
        _renderer.EndFrame();
    }

    public void Dispose() => _renderer.Dispose();
}