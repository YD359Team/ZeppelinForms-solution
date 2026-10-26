using System.Runtime.InteropServices;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing;

namespace ZeppelinForms.Windows.Rendering;

/// <summary>A DIB section the backend draws into on the CPU, copied onto the window
/// with BitBlt — the software fallback when there is no GL, or the backend draws
/// without it.</summary>
internal sealed class Win32DibSurface : IWin32Surface
{
    private readonly nint _hWnd;
    private readonly IGraphicsBackend _graphics;

    private nint _memDc;
    private nint _dib;
    private nint _oldBitmap;
    private nint _pixels;

    private int _width;
    private int _height;

    private IRenderSurface? _surface;

    public bool SupportsPartialRedraw => true;

    public Win32DibSurface(nint hWnd, IGraphicsBackend graphics)
    {
        _hWnd = hWnd;
        _graphics = graphics;
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        if (width == _width && height == _height)
            return;

        DestroySurface();

        nint screenDc = NativeMethods.GetDC(_hWnd);

        try
        {
            _memDc = NativeMethods.CreateCompatibleDC(screenDc);

            var header = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = width,
                // A negative height — a top-down DIB,
                // so that the rows go in the same order as in the backend's surface.
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeConstants.BI_RGB,
            };

            _dib = NativeMethods.CreateDIBSection(
                screenDc, ref header, NativeConstants.DIB_RGB_COLORS,
                out _pixels, 0, 0);

            _oldBitmap = NativeMethods.SelectObject(_memDc, _dib);
        }
        finally
        {
            NativeMethods.ReleaseDC(_hWnd, screenDc);
        }

        _width = width;
        _height = height;

        _surface = _graphics.CreatePixelSurface(
            new PixelBuffer(_pixels, width, height, width * 4, PixelFormat.Bgra8888))
            ?? throw new ZfPlatformException(
                $"The {_graphics.Name} backend draws on the GPU only, and there is no OpenGL on this window.");
    }

    public Graphics? BeginFrame() => _surface?.BeginFrame();

    public void EndFrame()
    {
        _surface!.EndFrame();

        nint windowDc = NativeMethods.GetDC(_hWnd);

        try
        {
            NativeMethods.BitBlt(
                windowDc, 0, 0, _width, _height,
                _memDc, 0, 0, NativeConstants.SRCCOPY);
        }
        finally
        {
            NativeMethods.ReleaseDC(_hWnd, windowDc);
        }
    }

    private void DestroySurface()
    {
        _surface?.Dispose();
        _surface = null;

        if (_memDc != 0)
        {
            if (_oldBitmap != 0)
                NativeMethods.SelectObject(_memDc, _oldBitmap);

            NativeMethods.DeleteDC(_memDc);
            _memDc = 0;
        }

        if (_dib != 0)
        {
            NativeMethods.DeleteObject(_dib);
            _dib = 0;
        }
    }

    public void Dispose() => DestroySurface();
}