using ZeppelinForms.Core;
using ZeppelinForms.Drawing;
using ZeppelinForms.Gles.Imaging;
using ZeppelinForms.Gles.Rendering;
using ZeppelinForms.Headless;
using ZeppelinForms.Gles.Text;

namespace ZeppelinForms.Gles;

/// <summary>
/// OpenGL ES 3 as a graphics backend: the forms drawn on the GPU by ZeppelinForms' own
/// renderer, without SkiaSharp and its native libraries. A preview in 0.18.
/// </summary>
/// <remarks>
/// <para>
/// It draws into a GL framebuffer only — the platform's EGL, ANGLE, WebGL2 or WGL context,
/// OpenGL ES 3.0 or desktop OpenGL 3.3 — and gives no surface over a buffer of pixels: a
/// platform without GL falls back to what it has.
/// </para>
/// <para>
/// Text is its own: the system's fonts read in managed code — TrueType, CFF, collections,
/// color emoji — found through fontconfig on Linux and the font folders elsewhere, shaped
/// by the system's HarfBuzz where there is one.
/// </para>
/// <para>
/// What the preview lacks: JPEG pictures don't decode (PNG and BMP do); blurs, backdrops,
/// reflections and blend modes other than normal are drawn without their effect. Each comes
/// in the next steps.
/// </para>
/// </remarks>
public sealed class GlesGraphicsBackend : IGraphicsBackend
{
    public static GlesGraphicsBackend Instance { get; } = new();

    private GlesGraphicsBackend() { }

    public string Name => "GLES";

    public void Register()
    {
        GlesTextMeasurer.Register();
        GlesImageDecoder.Register();
        HeadlessElementRenderer.Register();
    }

    /// <summary>None: the backend draws on the GPU only.</summary>
    public IRenderSurface? CreatePixelSurface(PixelBuffer buffer) => null;

    public IGlSurface? CreateGlSurface(GlTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.GetProcAddress is not { } getProcAddress)
            throw new ZfPlatformException("The GLES backend needs the context's GetProcAddress.");

        var gl = new Gl(getProcAddress);

        string version = gl.String(Gl.GL_VERSION);

        if (!SupportsVersion(version, target.IsGles))
        {
            throw new ZfPlatformException(
                $"The GLES backend needs OpenGL ES 3.0 or OpenGL 3.3; the context is \"{version}\".");
        }

        GlesRenderer renderer = new(gl, target.IsGles);

        try
        {
            var surface = new GlesSurface(renderer, target);
            surface.Resize(target.Width, target.Height);
            return surface;
        }
        catch
        {
            renderer.Dispose();
            throw;
        }
    }

    /// <summary>"OpenGL ES 3.2 Mesa 24.0" — ES 3 or later; "4.6 (Core Profile) Mesa" — 3.3 or later.</summary>
    internal static bool SupportsVersion(string version, bool isGles)
    {
        const string EsPrefix = "OpenGL ES ";

        string number = version.StartsWith(EsPrefix, StringComparison.Ordinal)
            ? version[EsPrefix.Length..]
            : version;

        int space = number.IndexOf(' ', StringComparison.Ordinal);
        if (space >= 0) number = number[..space];

        string[] parts = number.Split('.');

        if (parts.Length < 2 ||
            !int.TryParse(parts[0], out int major) ||
            !int.TryParse(new string(parts[1].TakeWhile(char.IsAsciiDigit).ToArray()), out int minor))
        {
            return false;
        }

        return isGles ? major >= 3 : major > 3 || (major == 3 && minor >= 3);
    }
}