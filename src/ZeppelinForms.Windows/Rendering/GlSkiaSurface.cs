using SkiaSharp;
using System.Runtime.InteropServices;

namespace ZeppelinForms.Windows.Rendering;

internal sealed class GlSkiaSurface : IWin32SkiaSurface
{
    private const uint GL_RGBA8 = 0x8058;

    private readonly nint _hWnd;
    private readonly nint _hdc;
    private readonly nint _glContext;
    private readonly GRContext _grContext;

    private SKSurface? _surface;
    private GRBackendRenderTarget? _renderTarget;

    private int _width;
    private int _height;

    private GlSkiaSurface(nint hWnd, nint hdc, nint glContext, GRContext grContext)
    {
        _hWnd = hWnd;
        _hdc = hdc;
        _glContext = glContext;
        _grContext = grContext;
    }

    // Бросает исключение, если GPU-контекст не поднялся —
    // Win32SkiaSurfaceFactory на этом ловит и откатывается на software.
    public static GlSkiaSurface Create(nint hWnd)
    {
        nint hdc = NativeMethods.GetDC(hWnd);

        var pfd = new NativeMethods.PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)Marshal.SizeOf<NativeMethods.PIXELFORMATDESCRIPTOR>(),
            nVersion = 1,
            dwFlags = NativeMethods.PFD_DRAW_TO_WINDOW
                | NativeMethods.PFD_SUPPORT_OPENGL
                | NativeMethods.PFD_DOUBLEBUFFER,
            iPixelType = NativeMethods.PFD_TYPE_RGBA,
            cColorBits = 32,
            cDepthBits = 24,
            cStencilBits = 8,
            iLayerType = NativeMethods.PFD_MAIN_PLANE,
        };

        int format = NativeMethods.ChoosePixelFormat(hdc, ref pfd);

        if (format == 0 || !NativeMethods.SetPixelFormat(hdc, format, ref pfd))
        {
            NativeMethods.ReleaseDC(hWnd, hdc);
            throw new InvalidOperationException("Не удалось настроить пиксельный формат.");
        }

        nint glContext = NativeMethods.wglCreateContext(hdc);

        if (glContext == 0 || !NativeMethods.wglMakeCurrent(hdc, glContext))
        {
            if (glContext != 0)
                NativeMethods.wglDeleteContext(glContext);

            NativeMethods.ReleaseDC(hWnd, hdc);
            throw new InvalidOperationException("Не удалось создать/активировать контекст OpenGL.");
        }

        using GRGlInterface glInterface = GRGlInterface.Create()
            ?? throw new InvalidOperationException("Не удалось создать GRGlInterface.");

        GRContext grContext = GRContext.CreateGl(glInterface)
            ?? throw new InvalidOperationException("Не удалось создать GRContext.");
        grContext.SetResourceCacheLimit(32 * 1024 * 1024);

        return new GlSkiaSurface(hWnd, hdc, glContext, grContext);
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        if (width == _width && height == _height && _surface is not null)
            return;

        _surface?.Dispose();
        _renderTarget?.Dispose();

        NativeMethods.wglMakeCurrent(_hdc, _glContext);

        var glInfo = new GRGlFramebufferInfo(0, GL_RGBA8);

        _renderTarget = new GRBackendRenderTarget(width, height, 0, 8, glInfo);

        _surface = SKSurface.Create(
            _grContext, _renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);

        _width = width;
        _height = height;
    }

    public SKSurface BeginFrame()
    {
        NativeMethods.wglMakeCurrent(_hdc, _glContext);

        return _surface ?? throw new InvalidOperationException("Вызовите Resize перед BeginFrame.");
    }

    public void EndFrame()
    {
        _surface!.Canvas.Flush();
        _grContext.Flush();

        NativeMethods.SwapBuffers(_hdc);
    }

    public void Dispose()
    {
        _surface?.Dispose();
        _renderTarget?.Dispose();
        _grContext.Dispose();

        NativeMethods.wglMakeCurrent(0, 0);
        NativeMethods.wglDeleteContext(_glContext);
        NativeMethods.ReleaseDC(_hWnd, _hdc);
    }
}
