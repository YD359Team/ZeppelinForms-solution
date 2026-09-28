using System.Runtime.InteropServices;
using SkiaSharp;

namespace ZeppelinForms.Browser;

/// <summary>
/// A frame is drawn by Skia into an unmanaged buffer, then goes onto the canvas
/// whole through putImageData. Partial redraw is not supported: ImageData in
/// a browser is transferred as a full frame anyway, and the gain would be only
/// on putImageData itself, not on the transfer.
/// </summary>
internal sealed class BrowserSkiaSurface : IDisposable
{
    private nint _pixels;
    private SKSurface? _surface;

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
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(info, _pixels, stride);
    }

    public SKSurface? BeginFrame() => _surface;

    public unsafe void EndFrame()
    {
        if (_surface is null || _pixels == 0) return;

        _surface.Canvas.Flush();

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