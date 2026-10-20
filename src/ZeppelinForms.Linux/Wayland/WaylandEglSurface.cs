using System.Runtime.InteropServices;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>
/// An EGL context and window surface on a Wayland surface, through a wl_egl_window, and the
/// backend's GL surface drawing into its framebuffer.
/// </summary>
/// <remarks>
/// <para>
/// eglSwapBuffers attaches, damages and commits the surface itself: what the window set
/// before it — the buffer scale, the frame callback — goes with that commit.
/// </para>
/// <para>
/// The swap interval is 0: with 1 a swap waits for the compositor's frame callback inside
/// EGL, and a window the compositor doesn't show — covered, minimized — blocked the
/// loop in its swap until shown again. The window paces itself by its own callbacks.
/// </para>
/// </remarks>
internal sealed class WaylandEglSurface : IWaylandSurface
{
    internal const string WaylandEglLib = "libwayland-egl.so.1";

    [DllImport(WaylandEglLib)] private static extern nint wl_egl_window_create(nint surface, int width, int height);
    [DllImport(WaylandEglLib)] private static extern void wl_egl_window_resize(nint window, int width, int height, int dx, int dy);
    [DllImport(WaylandEglLib)] private static extern void wl_egl_window_destroy(nint window);

    private readonly EglDisplay _egl;
    private readonly nint _context;
    private readonly nint _eglWindow;
    private readonly nint _surface;
    private readonly IGlSurface _backend;

    private int _width;
    private int _height;

    private WaylandEglSurface(EglDisplay egl, nint context, nint eglWindow, nint surface, IGlSurface backend)
    {
        _egl = egl;
        _context = context;
        _eglWindow = eglWindow;
        _surface = surface;
        _backend = backend;
    }

    /// <summary>A swap always has a buffer to draw into: EGL waits for one itself.</summary>
    public bool CanBeginFrame => true;

    /// <summary>The surface, or null when the backend doesn't draw with GL.</summary>
    /// <exception cref="ZfPlatformException">The GPU context didn't come up — the
    /// factory catches that and falls back to shared memory.</exception>
    public static WaylandEglSurface? Create(EglDisplay egl, WlSurface surface, IGraphicsBackend graphics)
    {
        (nint config, int esVersion) = egl.ChooseConfigWithAlpha()
            ?? throw new ZfPlatformException("EGL has no RGBA config for a Wayland surface.");

        nint context = Egl.eglCreateContext(
            egl.Handle, config, 0, [Egl.EGL_CONTEXT_CLIENT_VERSION, esVersion, Egl.EGL_NONE]);

        if (context == 0)
            throw new ZfPlatformException($"EGL could not create an OpenGL ES {esVersion} context ({Egl.LastError()}).");

        // 1 × 1 until the first frame says the size: a wl_egl_window can't be empty
        nint eglWindow = wl_egl_window_create(surface.Handle, 1, 1);

        if (eglWindow == 0)
        {
            Egl.eglDestroyContext(egl.Handle, context);
            throw new ZfPlatformException("libwayland-egl could not make a window for the surface.");
        }

        nint eglSurface = Egl.eglCreateWindowSurface(egl.Handle, config, eglWindow, [Egl.EGL_NONE]);

        if (eglSurface == 0)
        {
            string error = Egl.LastError();
            wl_egl_window_destroy(eglWindow);
            Egl.eglDestroyContext(egl.Handle, context);
            throw new ZfPlatformException($"EGL could not create the window's surface ({error}).");
        }

        try
        {
            if (Egl.eglMakeCurrent(egl.Handle, eglSurface, eglSurface, context) == 0)
                throw new ZfPlatformException($"EGL could not make the context current ({Egl.LastError()}).");

            Egl.eglSwapInterval(egl.Handle, 0);

            IGlSurface? backend = graphics.CreateGlSurface(new GlTarget
            {
                Width = 0,
                Height = 0,
                IsGles = true,
                StencilBits = 8,
                GetProcAddress = EglDisplay.GetProcAddress,
            });

            if (backend is null)
            {
                Release(egl, context, eglSurface, eglWindow);
                return null;
            }

            return new WaylandEglSurface(egl, context, eglWindow, eglSurface, backend);
        }
        catch
        {
            // the factory falls back to shared memory on this same surface: nothing of
            // EGL may stay attached to it
            Release(egl, context, eglSurface, eglWindow);
            throw;
        }
    }

    private static void Release(EglDisplay egl, nint context, nint surface, nint eglWindow)
    {
        Egl.eglMakeCurrent(egl.Handle, 0, 0, 0);
        Egl.eglDestroySurface(egl.Handle, surface);
        Egl.eglDestroyContext(egl.Handle, context);
        wl_egl_window_destroy(eglWindow);
    }

    private void MakeCurrent() => Egl.eglMakeCurrent(_egl.Handle, _surface, _surface, _context);

    public Graphics? BeginFrame(int width, int height)
    {
        if (width <= 0 || height <= 0) return null;

        MakeCurrent();

        // the next swap is of this size: the EGL window first, the backend's render
        // target after it
        if (width != _width || height != _height)
        {
            wl_egl_window_resize(_eglWindow, width, height, 0, 0);
            _backend.Resize(width, height);

            _width = width;
            _height = height;
        }

        return _backend.BeginFrame();
    }

    /// <summary>The swap commits the surface: the whole frame goes out.</summary>
    public void EndFrame(WlSurface surface, int width, int height)
    {
        _backend.EndFrame();

        Egl.eglSwapBuffers(_egl.Handle, _surface);
    }

    public void Dispose()
    {
        // the backend's GPU resources are freed in their own context
        MakeCurrent();
        _backend.Dispose();

        Release(_egl, _context, _surface, _eglWindow);
    }
}