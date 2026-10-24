using ZeppelinForms.Core;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Linux;

/// <summary>An EGL context and window surface on an X11 window, and the backend's GL
/// surface drawing into its framebuffer. The context is the platform's: made current
/// here before every call into the backend, swapped here after every frame.</summary>
internal sealed class X11EglSurface : IX11Surface
{
    private readonly EglDisplay _egl;
    private readonly nint _context;
    private readonly nint _surface;
    private readonly IGlSurface _backend;

    /// <summary>The backend's surface has a size: until the first resize there is
    /// no render target to draw into.</summary>
    private bool _sized;

    public bool SupportsPartialRedraw => false;

    private X11EglSurface(EglDisplay egl, nint context, nint surface, IGlSurface backend)
    {
        _egl = egl;
        _context = context;
        _surface = surface;
        _backend = backend;
    }

    /// <summary>The surface, or null when the backend doesn't draw with GL.</summary>
    /// <param name="visual">The window's visual: the config must be of the same one.</param>
    /// <exception cref="ZfPlatformException">The GPU context didn't come up — the
    /// factory catches that and falls back to software.</exception>
    public static X11EglSurface? Create(EglDisplay egl, nuint window, nint visual, IGraphicsBackend graphics)
    {
        int visualId = (int)X11.XVisualIDFromVisual(visual);

        (nint config, int esVersion) = egl.ChooseConfig(visualId)
            ?? throw new ZfPlatformException($"EGL has no config for the window's visual 0x{visualId:X}.");

        nint context = Egl.eglCreateContext(
            egl.Handle, config, 0, [Egl.EGL_CONTEXT_CLIENT_VERSION, esVersion, Egl.EGL_NONE]);

        if (context == 0)
            throw new ZfPlatformException($"EGL could not create an OpenGL ES {esVersion} context ({Egl.LastError()}).");

        nint surface = Egl.eglCreateWindowSurface(egl.Handle, config, (nint)window, [Egl.EGL_NONE]);

        if (surface == 0)
        {
            string error = Egl.LastError();
            Egl.eglDestroyContext(egl.Handle, context);
            throw new ZfPlatformException($"EGL could not create the window's surface ({error}).");
        }

        try
        {
            if (Egl.eglMakeCurrent(egl.Handle, surface, surface, context) == 0)
                throw new ZfPlatformException($"EGL could not make the context current ({Egl.LastError()}).");

            // as with WGL: a swap waiting for the vertical retrace would block the
            // thread that pumps the X events, and input would lag behind the frames
            Egl.eglSwapInterval(egl.Handle, 0);

            // the size comes with the first ConfigureNotify; the window's framebuffer,
            // the GL functions through EGL's loader
            IGlSurface? backend = graphics.CreateGlSurface(new GlTarget
            {
                Width = 0,
                Height = 0,
                IsGles = true,
                StencilBits = 8,
                GetProcAddress = EglDisplay.GetProcAddress,
                // an element drawn into a picture between frames, in this context
                MakeCurrent = () => Egl.eglMakeCurrent(egl.Handle, surface, surface, context),
            });

            if (backend is null)
            {
                Release(egl, context, surface);
                return null;
            }

            return new X11EglSurface(egl, context, surface, backend);
        }
        catch
        {
            // the factory falls back to an XImage on this same window: nothing of
            // EGL may stay bound to it
            Release(egl, context, surface);
            throw;
        }
    }

    private static void Release(EglDisplay egl, nint context, nint surface)
    {
        Egl.eglMakeCurrent(egl.Handle, 0, 0, 0);
        Egl.eglDestroySurface(egl.Handle, surface);
        Egl.eglDestroyContext(egl.Handle, context);
    }

    private void MakeCurrent() => Egl.eglMakeCurrent(_egl.Handle, _surface, _surface, _context);

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;

        // the EGL surface follows the X window's size by itself; only the backend's
        // render target is remade
        MakeCurrent();

        _backend.Resize(width, height);
        _sized = true;
    }

    public Graphics? BeginFrame()
    {
        if (!_sized) return null;

        MakeCurrent();
        return _backend.BeginFrame();
    }

    /// <summary>The whole frame goes out: a swap has no part of it.</summary>
    public void EndFrame(Rectangle? dirty = null)
    {
        _backend.EndFrame();

        Egl.eglSwapBuffers(_egl.Handle, _surface);
    }

    public void Dispose()
    {
        // the backend's GPU resources are freed in their own context
        MakeCurrent();
        _backend.Dispose();

        Release(_egl, _context, _surface);
    }
}