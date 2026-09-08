using System.Runtime.InteropServices;
using SkiaSharp;

namespace ZeppelinForms.Browser;

/// <summary>
/// Кадр рисуется Skia в неуправляемый буфер, затем целиком уходит на canvas
/// через putImageData. Частичная перерисовка не поддерживается: ImageData
/// в браузере всё равно передаётся полным кадром, и выигрыш был бы только
/// на самом putImageData, а не на передаче.
/// </summary>
internal sealed class BrowserSkiaSurface : IDisposable
{
    private nint _pixels;
    private SKSurface? _surface;

    private int _width;
    private int _height;

    public bool SupportsPartialRedraw => false;

    /// <summary>Размер физический: canvas.width, а не CSS-ширина.</summary>
    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (width == _width && height == _height && _surface is not null) return;

        Release();

        _width = width;
        _height = height;

        int stride = width * 4;
        _pixels = Marshal.AllocHGlobal(stride * height);

        // Rgba8888, а не Bgra: ImageData в браузере хранит байты в порядке RGBA.
        // Premul при непрозрачном кадре совпадает с Unpremul, а Unpremul
        // растровая поверхность Skia поддерживает не везде
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