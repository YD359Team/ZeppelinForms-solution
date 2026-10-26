using System.Runtime.InteropServices;

namespace ZeppelinForms.Linux;

/// <summary>The EGL calls the platform makes: a display over the native one, a context,
/// a window surface, swapping. Common to X11 and, later, Wayland — only the native
/// display and window differ.</summary>
/// <remarks>libEGL.so.1 is loaded on the first call: a machine without it fails that
/// call with a DllNotFoundException, which the surface factory takes as "no GPU here"
/// and falls back to drawing in memory.</remarks>
internal static class Egl
{
    internal const string Lib = "libEGL.so.1";

    public const int EGL_SUCCESS = 0x3000;
    public const int EGL_NONE = 0x3038;

    public const int EGL_BLUE_SIZE = 0x3022;
    public const int EGL_ALPHA_SIZE = 0x3021;
    public const int EGL_GREEN_SIZE = 0x3023;
    public const int EGL_RED_SIZE = 0x3024;
    public const int EGL_STENCIL_SIZE = 0x3026;
    public const int EGL_NATIVE_VISUAL_ID = 0x302E;
    public const int EGL_SURFACE_TYPE = 0x3033;
    public const int EGL_RENDERABLE_TYPE = 0x3040;

    public const int EGL_WINDOW_BIT = 0x0004;
    public const int EGL_OPENGL_ES2_BIT = 0x0004;
    public const int EGL_OPENGL_ES3_BIT = 0x0040;

    public const int EGL_EXTENSIONS = 0x3055;
    public const int EGL_CONTEXT_CLIENT_VERSION = 0x3098;
    public const uint EGL_OPENGL_ES_API = 0x30A0;

    /// <summary>EGL_KHR_platform_x11 / EGL_EXT_platform_x11.</summary>
    public const uint EGL_PLATFORM_X11_KHR = 0x31D5;

    /// <summary>EGL_KHR_platform_wayland / EGL_EXT_platform_wayland.</summary>
    public const uint EGL_PLATFORM_WAYLAND_KHR = 0x31D8;

    [DllImport(Lib)] public static extern int eglGetError();

    [DllImport(Lib)] public static extern nint eglGetDisplay(nint nativeDisplay);

    [DllImport(Lib)] public static extern uint eglInitialize(nint display, out int major, out int minor);

    [DllImport(Lib)] public static extern uint eglTerminate(nint display);

    /// <summary>A string of the display; with no display (0) and EGL_EXTENSIONS, the
    /// client extensions — or null on an EGL older than EGL_EXT_client_extensions.</summary>
    [DllImport(Lib)] public static extern nint eglQueryString(nint display, int name);

    [DllImport(Lib)] public static extern uint eglBindAPI(uint api);

    [DllImport(Lib)]
    public static extern uint eglChooseConfig(nint display, int[] attribs, nint[]? configs, int size, out int count);

    [DllImport(Lib)] public static extern uint eglGetConfigAttrib(nint display, nint config, int attribute, out int value);

    [DllImport(Lib)] public static extern nint eglCreateContext(nint display, nint config, nint shareContext, int[] attribs);

    [DllImport(Lib)] public static extern uint eglDestroyContext(nint display, nint context);

    [DllImport(Lib)] public static extern nint eglCreateWindowSurface(nint display, nint config, nint nativeWindow, int[] attribs);

    [DllImport(Lib)] public static extern uint eglDestroySurface(nint display, nint surface);

    [DllImport(Lib)] public static extern uint eglMakeCurrent(nint display, nint draw, nint read, nint context);

    [DllImport(Lib)] public static extern uint eglSwapBuffers(nint display, nint surface);

    [DllImport(Lib)] public static extern uint eglSwapInterval(nint display, int interval);

    [DllImport(Lib)] public static extern nint eglGetProcAddress([MarshalAs(UnmanagedType.LPStr)] string name);

    /// <summary>The last error as EGL names it, for a message.</summary>
    public static string LastError() => $"0x{eglGetError():X4}";

    /// <summary>The client extensions: what eglGetPlatformDisplayEXT can be asked for.</summary>
    public static string ClientExtensions()
    {
        nint extensions = eglQueryString(0, EGL_EXTENSIONS);

        return extensions != 0 ? Marshal.PtrToStringAnsi(extensions) ?? string.Empty : string.Empty;
    }
}