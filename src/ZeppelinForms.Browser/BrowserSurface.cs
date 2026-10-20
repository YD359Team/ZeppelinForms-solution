using System.Runtime.InteropServices;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing;

namespace ZeppelinForms.Browser;

/// <summary>
/// A frame is drawn by the graphics backend into an unmanaged buffer, then goes onto the canvas
/// whole through putImageData. Partial redraw is not supported: ImageData in
/// a browser is transferred as a full frame anyway, and the gain would be only
/// on putImageData itself, not on the transfer.
/// </summary>
internal sealed class BrowserSurface(IGraphicsBackend graphics) : IDisposable
{
    private nint _pixels;
    private IRenderSurface? _surface;

    private int _width;
    private int _height;

    public bool SupportsPartialRedraw => false;

    /// <summary>The size is physical: canvas.width, not the CSS width.</summary>
    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (width == _width && height == _height && _surface is not null) return;

        Release();

        _width = width;
        _height = height;

        int stride = width * 4;
        _pixels = Marshal.AllocHGlobal(stride * height);

        // Rgba8888, not Bgra: ImageData in a browser stores bytes in RGBA order.
        // Premul equals Unpremul for an opaque frame, while an Unpremul raster
        // surface is not supported by Skia everywhere
        _surface = graphics.CreatePixelSurface(
            new PixelBuffer(_pixels, width, height, stride, PixelFormat.Rgba8888))
            ?? throw new ZfPlatformException(
                $"The {graphics.Name} backend draws on the GPU only, and the browser has no WebGL surface yet.");
    }

    public Graphics? BeginFrame() => _surface?.BeginFrame();

    public unsafe void EndFrame()
    {
        if (_surface is null || _pixels == 0) return;

        _surface.EndFrame();

        var pixels = new Span<byte>((void*)_pixels, _width * 4 * _height);
        Interop.Present(pixels, _width, _height);
    }

    private void Release()
    {
        _surface?.Dispose();
        _surface = null;

        if (_pixels != 0)
        {
            Marshal.FreeHGlobal(_pixels);
            _pixels = 0;
        }

        _width = 0;
        _height = 0;
    }

    public void Dispose() => Release();
}