namespace ZeppelinForms.Drawing;

/// <summary>
/// What draws the forms: Skia, GLES, the headless no-op. A platform gives the backend
/// a place to draw into — a buffer of pixels in memory or an OpenGL framebuffer — and
/// gets back a surface it draws each frame through.
/// </summary>
/// <remarks>
/// <para>
/// The platform owns the window and the presentation: it allocates the buffer and
/// copies it to the screen, or makes the GL context current and swaps it. The backend
/// owns the drawing only. That split is what lets one backend serve every platform and
/// one platform take any backend.
/// </para>
/// <para>
/// A backend that can't draw into one kind of target returns null for it, and the
/// platform falls back to another kind: a GPU-only backend gives no pixel surface, a
/// CPU-only one no GL surface. A target it can draw into but couldn't set up — a
/// GL context without the functions it needs — throws <see cref="Core.ZfPlatformException"/>.
/// </para>
/// </remarks>
public interface IGraphicsBackend
{
    /// <summary>The name shown in diagnostics: "Skia", "GLES", "Headless".</summary>
    string Name { get; }

    /// <summary>Install the services that measure text, decode pictures and render
    /// elements offscreen the way this backend draws them. A platform calls it once,
    /// as it is made, before any form is measured.</summary>
    void Register();

    /// <summary>A surface drawing into a buffer of pixels in memory, or null when the
    /// backend draws on the GPU only.</summary>
    IRenderSurface? CreatePixelSurface(PixelBuffer buffer);

    /// <summary>A surface drawing into the framebuffer of the GL context current on
    /// this thread, or null when the backend doesn't draw with GL.</summary>
    /// <exception cref="Core.ZfPlatformException">The backend draws with GL but couldn't
    /// set itself up in this context.</exception>
    IGlSurface? CreateGlSurface(GlTarget target);
}