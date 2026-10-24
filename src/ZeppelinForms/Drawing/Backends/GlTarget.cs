namespace ZeppelinForms.Drawing;

/// <summary>The GL context a platform made and keeps current while a frame is drawn:
/// what a backend needs to know to draw into its framebuffer.</summary>
/// <remarks>The platform creates the context, makes it current before every call into
/// the surface and swaps the buffers after <see cref="IRenderSurface.EndFrame"/>; the
/// backend never does either.</remarks>
public sealed class GlTarget
{
    /// <summary>The framebuffer's width in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>The framebuffer's height in pixels.</summary>
    public required int Height { get; init; }

    /// <summary>OpenGL ES (EGL, ANGLE, WebGL) rather than desktop OpenGL (WGL, GLX).</summary>
    public bool IsGles { get; init; }

    /// <summary>The framebuffer object to draw into; 0 is the window's own.</summary>
    public uint Framebuffer { get; init; }

    /// <summary>The samples per pixel of the framebuffer; 0 — no multisampling.</summary>
    public int SampleCount { get; init; }

    /// <summary>The bits of its stencil buffer.</summary>
    public int StencilBits { get; init; } = 8;

    /// <summary>The address of a GL function by name, as the context's loader gives it —
    /// eglGetProcAddress, wglGetProcAddress with the opengl32 fallback. Null: the
    /// backend finds them its own way. Skia's backend takes it for OpenGL ES only: on
    /// desktop OpenGL it keeps to its own native interface.</summary>
    public Func<string, nint>? GetProcAddress { get; init; }

    /// <summary>Make the context current on the calling thread, outside the frames the
    /// platform begins: for a backend drawing an element into a picture —
    /// <see cref="Forms.Controls.Base.UIElement.RenderToImage"/>, the color picker's
    /// eyedropper. Null: the backend draws in the platform's frames only.</summary>
    /// <remarks>Called on the window's thread; the next frame makes the context current
    /// again by itself, as every frame does.</remarks>
    public Action? MakeCurrent { get; init; }
}