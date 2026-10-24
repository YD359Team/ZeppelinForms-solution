using System.Runtime.InteropServices;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing;

namespace ZeppelinForms.Windows.Rendering;

/// <summary>A WGL context on the window, and the backend's GL surface drawing into its
/// framebuffer. The context is the platform's: made current here before every call
/// into the backend, swapped here after every frame.</summary>
internal sealed class Win32GlSurface : IWin32Surface
{
    private readonly nint _hWnd;
    private readonly nint _hdc;
    private readonly nint _glContext;
    private readonly IGlSurface _surface;

    /// <summary>The backend's surface has a size: until the first resize there is
    /// no render target to draw into.</summary>
    private bool _sized;

    public bool SupportsPartialRedraw => false;

    private Win32GlSurface(nint hWnd, nint hdc, nint glContext, IGlSurface surface)
    {
        _hWnd = hWnd;
        _hdc = hdc;
        _glContext = glContext;
        _surface = surface;
    }

    /// <summary>The surface, or null when the backend doesn't draw with GL.</summary>
    /// <exception cref="ZfPlatformException">The GPU context didn't come up —
    /// Win32SurfaceFactory catches that and falls back to software.</exception>
    public static Win32GlSurface? Create(nint hWnd, IGraphicsBackend graphics)
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
            throw new ZfPlatformException("Could not set up the pixel format.");
        }

        nint glContext = NativeMethods.wglCreateContext(hdc);

        if (glContext == 0 || !NativeMethods.wglMakeCurrent(hdc, glContext))
        {
            if (glContext != 0)
                NativeMethods.wglDeleteContext(glContext);

            NativeMethods.ReleaseDC(hWnd, hdc);
            throw new ZfPlatformException("Could not create or activate the OpenGL context.");
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

            // the size comes with the first WM_SIZE; the functions through WGL's loader,
            // as Skia's own WGL interface finds them — the GLES backend has no other way
            IGlSurface? surface = graphics.CreateGlSurface(new GlTarget
            {
                Width = 0,
                Height = 0,
                IsGles = false,
                StencilBits = 8,
                GetProcAddress = GetGlProcAddress,
                // an element drawn into a picture between frames, in this context
                MakeCurrent = () => NativeMethods.wglMakeCurrent(hdc, glContext),
            });

            if (surface is null)
            {
                Release(hWnd, hdc, glContext);
                return null;
            }

            return new Win32GlSurface(hWnd, hdc, glContext, surface);
        }
        catch
        {
            // the factory falls back to software on this same window: the context
            // must not stay current and alive, nor the DC held
            Release(hWnd, hdc, glContext);
            throw;
        }
    }

    private static void Release(nint hWnd, nint hdc, nint glContext)
    {
        NativeMethods.wglMakeCurrent(0, 0);
        NativeMethods.wglDeleteContext(glContext);
        NativeMethods.ReleaseDC(hWnd, hdc);
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        NativeMethods.wglMakeCurrent(_hdc, _glContext);

        _surface.Resize(width, height);
        _sized = true;
    }

    public Graphics? BeginFrame()
    {
        if (!_sized) return null;

        NativeMethods.wglMakeCurrent(_hdc, _glContext);
        return _surface.BeginFrame();
    }

    public void EndFrame()
    {
        _surface.EndFrame();

        NativeMethods.SwapBuffers(_hdc);
    }

    public void Dispose()
    {
        // the backend's GPU resources are freed in their own context
        NativeMethods.wglMakeCurrent(_hdc, _glContext);
        _surface.Dispose();

        Release(_hWnd, _hdc, _glContext);
    }

    private static readonly Lazy<nint> OpenGl32 = new(() => NativeLibrary.Load("opengl32.dll"));

    /// <summary>A GL function's address on WGL: wglGetProcAddress for what came after
    /// OpenGL 1.1, opengl32.dll's own exports for 1.1 itself — for those the loader gives
    /// 0, or 1, 2, 3 or −1 on some drivers.</summary>
    private static nint GetGlProcAddress(string name)
    {
        nint address = NativeMethods.wglGetProcAddress(name);

        if (address is 0 or 1 or 2 or 3 or -1)
            address = NativeLibrary.TryGetExport(OpenGl32.Value, name, out nint export) ? export : 0;

        return address;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool SwapIntervalDelegate(int interval);
}