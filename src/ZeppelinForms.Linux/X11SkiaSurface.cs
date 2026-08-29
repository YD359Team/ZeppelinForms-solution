using SkiaSharp;
using System.Runtime.InteropServices;

namespace ZeppelinForms.Linux;

internal sealed class X11SkiaSurface : IDisposable
{
    private const int ZPixmap = 2;

    private readonly nint _display;
    private readonly nuint _window;
    private readonly nint _gc;
    private readonly nint _visual;
    private readonly uint _depth;

    private nint _pixels;
    private nint _image;
    private SKSurface? _surface;

    private int _width;
    private int _height;

    public X11SkiaSurface(nint display, nuint window, nint gc, nint visual, uint depth)
    {
        _display = display;
        _window = window;
        _gc = gc;
        _visual = visual;
        _depth = depth;
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (width == _width && height == _height && _surface is not null) return;

        Release();

        _width = width;
        _height = height;

        int stride = width * 4;
        _pixels = Marshal.AllocHGlobal(stride * height);

        // XCreateImage забирает буфер во владение только на чтение —
        // освобождать его должны мы сами, но XDestroyImage попытается
        // сделать free(data), поэтому образ не уничтожаем, а пересоздаём
        _image = X11.XCreateImage(
            _display, _visual, _depth, ZPixmap, 0,
            _pixels, (uint)width, (uint)height, 32, stride);

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(info, _pixels, stride);
    }

    public SKSurface? BeginFrame() => _surface;

    public void EndFrame()
    {
        if (_surface is null || _image == 0) return;

        _surface.Canvas.Flush();

        X11.XPutImage(_display, _window, _gc, _image, 0, 0, 0, 0, (uint)_width, (uint)_height);
        X11.XFlush(_display);
    }

    private void Release()
    {
        _surface?.Dispose();
        _surface = null;

        _image = 0;   // структура XImage останется висеть — см. комментарий ниже

        if (_pixels != 0)
        {
            Marshal.FreeHGlobal(_pixels);
            _pixels = 0;
        }
    }

    public void Dispose() => Release();
}
