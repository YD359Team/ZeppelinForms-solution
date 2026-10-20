using System.Runtime.InteropServices;
using ZeppelinForms.Core;

namespace ZeppelinForms.Linux;

/// <summary>An initialized EGL display over a native one, bound to OpenGL ES: one per
/// connection to the server, shared by every window's context.</summary>
/// <remarks>OpenGL ES rather than desktop GL: it is what every EGL driver gives —
/// Mesa, NVIDIA, the ARM boards — and the very API the GLES backend draws with. Skia
/// draws with it as well as with desktop GL.</remarks>
internal sealed class EglDisplay
{
    private static nint s_gles;

    private EglDisplay(nint handle) => Handle = handle;

    public nint Handle { get; }

    /// <summary>The display over an X11 connection.</summary>
    /// <exception cref="ZfPlatformException">EGL is there but won't initialize.</exception>
    /// <exception cref="DllNotFoundException">There is no libEGL on the machine.</exception>
    public static unsafe EglDisplay ForX11(nint x11Display)
    {
        nint display = 0;

        // the platform said explicitly where it can: a bare eglGetDisplay guesses the
        // kind of the native display from its first bytes, and with EGL_PLATFORM=wayland
        // in the environment it guessed Wayland for an X11 connection
        string extensions = Egl.ClientExtensions();

        if (extensions.Contains("EGL_EXT_platform_x11", StringComparison.Ordinal)
            || extensions.Contains("EGL_KHR_platform_x11", StringComparison.Ordinal))
        {
            nint getPlatformDisplay = Egl.eglGetProcAddress("eglGetPlatformDisplayEXT");

            if (getPlatformDisplay != 0)
            {
                var get = (delegate* unmanaged<uint, nint, int*, nint>)getPlatformDisplay;
                display = get(Egl.EGL_PLATFORM_X11_KHR, x11Display, null);
            }
        }

        if (display == 0)
            display = Egl.eglGetDisplay(x11Display);

        if (display == 0)
            throw new ZfPlatformException($"EGL gave no display for the X11 connection ({Egl.LastError()}).");

        if (Egl.eglInitialize(display, out _, out _) == 0)
            throw new ZfPlatformException($"EGL could not initialize its display ({Egl.LastError()}).");

        if (Egl.eglBindAPI(Egl.EGL_OPENGL_ES_API) == 0)
            throw new ZfPlatformException($"EGL has no OpenGL ES ({Egl.LastError()}).");

        return new EglDisplay(display);
    }

    /// <summary>A config for a window of the given visual: 8 bits a channel, a stencil
    /// for the paths, OpenGL ES 3 if the driver has it and 2 otherwise.</summary>
    /// <remarks>The visual must be the window's own: a surface of a config with another
    /// visual is refused with EGL_BAD_MATCH.</remarks>
    /// <returns>The config and the ES version for its context, or null when none
    /// matches the visual.</returns>
    public (nint Config, int EsVersion)? ChooseConfig(int visualId)
    {
        (int Version, int Bit)[] apis = [(3, Egl.EGL_OPENGL_ES3_BIT), (2, Egl.EGL_OPENGL_ES2_BIT)];

        foreach ((int version, int bit) in apis)
        {
            int[] attribs =
            [
                Egl.EGL_SURFACE_TYPE, Egl.EGL_WINDOW_BIT,
                Egl.EGL_RENDERABLE_TYPE, bit,
                Egl.EGL_RED_SIZE, 8,
                Egl.EGL_GREEN_SIZE, 8,
                Egl.EGL_BLUE_SIZE, 8,
                Egl.EGL_STENCIL_SIZE, 8,
                Egl.EGL_NONE,
            ];

            if (Egl.eglChooseConfig(Handle, attribs, null, 0, out int count) == 0 || count == 0)
                continue;

            var configs = new nint[count];

            if (Egl.eglChooseConfig(Handle, attribs, configs, count, out count) == 0)
                continue;

            // the best first, as EGL sorts them; the first of the window's visual wins
            for (int i = 0; i < count; i++)
            {
                if (Egl.eglGetConfigAttrib(Handle, configs[i], Egl.EGL_NATIVE_VISUAL_ID, out int id) != 0 && id == visualId)
                    return (configs[i], version);
            }
        }

        return null;
    }

    /// <summary>The address of a GL function for the backend's loader.</summary>
    /// <remarks>eglGetProcAddress gives the core functions too only where the driver has
    /// EGL_KHR_get_all_proc_addresses — Mesa and NVIDIA do; elsewhere the core ones are
    /// taken from libGLESv2 itself.</remarks>
    public static nint GetProcAddress(string name)
    {
        nint address = Egl.eglGetProcAddress(name);
        if (address != 0) return address;

        if (s_gles == 0 && !NativeLibrary.TryLoad("libGLESv2.so.2", out s_gles))
            return 0;

        return NativeLibrary.TryGetExport(s_gles, name, out address) ? address : 0;
    }
}