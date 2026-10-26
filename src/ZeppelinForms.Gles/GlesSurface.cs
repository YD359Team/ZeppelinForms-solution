using ZeppelinForms.Core;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Gles.Rendering;

namespace ZeppelinForms.Gles;

/// <summary>A surface drawing into the framebuffer of a platform's GL context: the program,
/// the atlases and the pictures' textures live as long as the window, a resize changes
/// only the size.</summary>
/// <remarks>The surfaces alive are known to the backend: an element drawn into a picture
/// between frames is drawn in one of their contexts, see <see cref="RenderOffscreen"/>.</remarks>
internal sealed class GlesSurface : IGlSurface
{
    private static readonly List<GlesSurface> Live = [];

    private readonly GlesRenderer _renderer;
    private readonly GlesGraphics _graphics;
    private readonly uint _framebuffer;
    private readonly Action? _makeCurrent;

    /// <summary>A renderer of its own for pictures drawn between frames, in this context:
    /// the window's keeps its atlases and its state.</summary>
    private GlesRenderer? _offscreen;

    private int _width;
    private int _height;
    private bool _inFrame;

    public GlesSurface(GlesRenderer renderer, GlTarget target)
    {
        _renderer = renderer;
        _graphics = new GlesGraphics(renderer);
        _framebuffer = target.Framebuffer;
        _makeCurrent = target.MakeCurrent;
        _width = target.Width;
        _height = target.Height;

        lock (Live) Live.Add(this);
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

    #region Offscreen

    /// <summary>A surface to draw a picture with: one in the middle of its frame — its
    /// context is current — or else one whose platform can make its context current.</summary>
    public static GlesSurface? ForOffscreen()
    {
        lock (Live)
        {
            GlesSurface? drawing = Live.FindLast(static s => s._inFrame);
            return drawing ?? Live.FindLast(static s => s._makeCurrent is not null);
        }
    }

    /// <summary>A picture drawn in this surface's context, into a framebuffer of its own,
    /// read back: RGBA, premultiplied, the top row first.</summary>
    /// <remarks>Between frames the platform's callback makes the context current; in the
    /// middle of this surface's frame it is current already, and the frame's state is set
    /// again after.</remarks>
    public Image RenderOffscreen(int width, int height, Action<Graphics> draw)
    {
        if (!_inFrame) _makeCurrent?.Invoke();

        Gl gl = _renderer.Gl;
        int limit = _renderer.MaxTextureSize;

        if (width > limit || height > limit)
        {
            throw new ZfPlatformException(
                $"A {width}×{height} picture is larger than the GPU's textures, {limit}×{limit} at most.");
        }

        _offscreen ??= new GlesRenderer(gl, _renderer.IsGles);

        LayerTexture framebuffer = LayerTexture.Create(gl, width, height);

        try
        {
            var target = new RenderTarget(framebuffer.Framebuffer, width, height, 0, 0);
            var graphics = new GlesGraphics(_offscreen);

            _offscreen.BeginFrame(target);
            graphics.Begin(target);
            _offscreen.Clear(default, target.Bounds);

            try
            {
                draw(graphics);
            }
            finally
            {
                graphics.End();
            }

            byte[] pixels = new byte[width * height * 4];
            _offscreen.ReadPixels(0, 0, width, height, pixels);
            _offscreen.EndFrame();

            return new Image(width, height, pixels);
        }
        finally
        {
            framebuffer.Delete(gl);

            // drawn in the middle of the window's frame: its state is its own again
            if (_inFrame) _renderer.RestoreState();
        }
    }

    #endregion

    public void Dispose()
    {
        lock (Live) Live.Remove(this);

        _offscreen?.Dispose();
        _renderer.Dispose();
    }
}