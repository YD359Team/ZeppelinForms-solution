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

    public bool SupportsPartialRedraw => false;

    private GlSkiaSurface(nint hWnd, nint hdc, nint glContext, GRContext grContext)
    {
        _hWnd = hWnd;
        _hdc = hdc;
        _glContext = glContext;
        _grContext = grContext;
    }

    // Throws if the GPU context didn't come up —
    // Win32SkiaSurfaceFactory catches that and falls back to software.
    public static GlSkiaSurface Create(nint hWnd)
    {
        nint hdc = NativeMethods.GetDC(hWnd);

        var pfd = new NativeMethods.PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)Marshal.SizeOf<NativeMethods.PIXELFORMATDESCRIPTOR>(),
            nVersion = 1,
            dwFlags = NativeConstants.PFD_DRAW_TO_WINDOW
                | NativeConstants.PFD_SUPPORT_OPENGL
                | NativeConstants.PFD_DOUBLEBUFFER,
            iPixelType = NativeConstants.PFD_TYPE_RGBA,
            cColorBits = 32,
            cDepthBits = 24,
            cStencilBits = 8,
            iLayerType = NativeConstants.PFD_MAIN_PLANE,
        };

        int format = NativeMethods.ChoosePixelFormat(hdc, ref pfd);

        if (format == 0 || !NativeMethods.SetPixelFormat(hdc, format, ref pfd))
        {
            NativeMethods.ReleaseDC(hWnd, hdc);
            throw new InvalidOperationException("Could not set up the pixel format.");
        }

        nint glContext = NativeMethods.wglCreateContext(hdc);

        if (glContext == 0 || !NativeMethods.wglMakeCurrent(hdc, glContext))
        {
            if (glContext != 0)
                NativeMethods.wglDeleteContext(glContext);

            NativeMethods.ReleaseDC(hWnd, hdc);
            throw new InvalidOperationException("Could not create or activate the OpenGL context.");
        }

        try
        {
            // after wglMakeCurrent, not before: wglGetProcAddress returns extension
            // addresses only for a current context. It used to be called earlier,
            // got 0, and the swap interval was never set
            nint swapInterval = NativeMethods.wglGetProcAddress("wglSwapIntervalEXT");

            if (swapInterval != 0)
            {
                var setSwapInterval = Marshal.GetDelegateForFunctionPointer<SwapIntervalDelegate>(swapInterval);

                // without this SwapBuffers blocks the thread until vertical retrace,
                // and WM_TIMER — a lowest-priority message — gets lost in the queue
                setSwapInterval(0);
            }

            using GRGlInterface glInterface = GRGlInterface.Create()
                ?? throw new InvalidOperationException("Could not create GRGlInterface.");

            GRContext grContext = GRContext.CreateGl(glInterface)
                ?? throw new InvalidOperationException("Could not create GRContext.");
            grContext.SetResourceCacheLimit(32 * 1024 * 1024);

            return new GlSkiaSurface(hWnd, hdc, glContext, grContext);
        }
        catch
        {
            // the factory falls back to software on this same window: the context
            // must not stay current and alive, nor the DC held
            NativeMethods.wglMakeCurrent(0, 0);
            NativeMethods.wglDeleteContext(glContext);
            NativeMethods.ReleaseDC(hWnd, hdc);

            throw;
        }
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

    public SKSurface? BeginFrame()
    {
        NativeMethods.wglMakeCurrent(_hdc, _glContext);
        return _surface;
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool SwapIntervalDelegate(int interval);
}