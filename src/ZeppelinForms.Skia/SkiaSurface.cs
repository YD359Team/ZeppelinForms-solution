using SkiaSharp;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing;

namespace ZeppelinForms.Skia;

/// <summary>A Skia surface over a platform's buffer of pixels: made anew for every
/// buffer, it owns nothing but the wrapper.</summary>
internal sealed class SkiaPixelSurface(SKSurface surface) : IRenderSurface
{
    public Graphics BeginFrame() => SkiaFrame.Begin(surface.Canvas);

    public void EndFrame() => surface.Canvas.Flush();

    public void Dispose() => surface.Dispose();
}

/// <summary>A Skia surface over a GL framebuffer: the context — its caches and atlases —
/// lives as long as the window, the render target is remade on every resize.</summary>
internal sealed class SkiaGlSurface(GRContext grContext, GlTarget target) : IGlSurface
{
    /// <summary>The internal format of the window's framebuffer.</summary>
    private const uint GL_RGBA8 = 0x8058;

    private SKSurface? _surface;
    private GRBackendRenderTarget? _renderTarget;

    private int _width;
    private int _height;

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (width == _width && height == _height && _surface is not null) return;

        ReleaseTarget();

        var glInfo = new GRGlFramebufferInfo(target.Framebuffer, GL_RGBA8);

        _renderTarget = new GRBackendRenderTarget(width, height, target.SampleCount, target.StencilBits, glInfo);

        _surface = SKSurface.Create(
            grContext, _renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
            ?? throw new ZfPlatformException($"Skia could not make a {width}×{height} GL surface.");

        _width = width;
        _height = height;
    }

    public Graphics BeginFrame() =>
        SkiaFrame.Begin((_surface ?? throw new InvalidOperationException("The GL surface has no size yet.")).Canvas);

    public void EndFrame()
    {
        if (_surface is null) return;

        _surface.Canvas.Flush();
        grContext.Flush();
    }

    private void ReleaseTarget()
    {
        _surface?.Dispose();
        _surface = null;

        _renderTarget?.Dispose();
        _renderTarget = null;
    }

    public void Dispose()
    {
        ReleaseTarget();
        grContext.Dispose();
    }
}

internal static class SkiaFrame
{
    /// <summary>The canvas bare, as a frame begins.</summary>
    /// <remarks>A GL surface keeps its canvas between frames, and a frame broken off by
    /// an exception left its saves behind: every next frame drew with that frame's scale
    /// and clip. Back to the first save count, whatever came before.</remarks>
    public static SkiaGraphics Begin(SKCanvas canvas)
    {
        canvas.RestoreToCount(1);
        canvas.ResetMatrix();

        return new SkiaGraphics(canvas);
    }
}