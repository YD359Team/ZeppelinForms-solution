using SkiaSharp;
using System.Runtime.InteropServices;

namespace ZeppelinForms.Windows.Rendering;

internal sealed class SoftwareSkiaSurface : IWin32SkiaSurface
{
    private readonly nint _hWnd;

    private nint _memDc;
    private nint _dib;
    private nint _oldBitmap;
    private nint _pixels;

    private int _width;
    private int _height;

    private SKSurface? _surface;

    public SoftwareSkiaSurface(nint hWnd) => _hWnd = hWnd;

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
                // Отрицательная высота — top-down DIB,
                // чтобы строки шли в том же порядке, что и в SKSurface.
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

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(info, _pixels, width * 4);
    }

    public SKSurface? BeginFrame() => _surface;

    public void EndFrame()
    {
        _surface!.Canvas.Flush();

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
