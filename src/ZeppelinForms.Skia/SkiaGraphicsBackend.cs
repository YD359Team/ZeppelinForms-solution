using SkiaSharp;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing;

namespace ZeppelinForms.Skia;

/// <summary>Skia as a graphics backend: it draws into a buffer of pixels on the CPU
/// and into a GL framebuffer on the GPU, and measures, shapes and decodes with Skia
/// and HarfBuzz.</summary>
public sealed class SkiaGraphicsBackend : IGraphicsBackend
{
    /// <summary>The GPU memory Skia may keep for its caches — textures, glyph atlases,
    /// paths. The default of 256 MB is a game's budget, not a form's.</summary>
    private const long ResourceCacheLimit = 32 * 1024 * 1024;

    public static SkiaGraphicsBackend Instance { get; } = new();

    private SkiaGraphicsBackend() { }

    public string Name => "Skia";

    public void Register()
    {
        SkiaImageDecoder.Register();
        SkiaTextMeasurer.Register();
        SkiaOffscreenRenderer.Register();
    }

    public IRenderSurface? CreatePixelSurface(PixelBuffer buffer)
    {
        SKColorType colorType = buffer.Format == PixelFormat.Rgba8888
            ? SKColorType.Rgba8888
            : SKColorType.Bgra8888;

        var info = new SKImageInfo(buffer.Width, buffer.Height, colorType, SKAlphaType.Premul);

        SKSurface surface = SKSurface.Create(info, buffer.Pixels, buffer.Stride)
            ?? throw new ZfPlatformException(
                $"Skia could not wrap a {buffer.Width}×{buffer.Height} buffer of pixels.");

        return new SkiaPixelSurface(surface);
    }

    public IGlSurface? CreateGlSurface(GlTarget target)
    {
        GRGlInterface? glInterface = target.GetProcAddress is { } getProcAddress
            ? target.IsGles
                ? GRGlInterface.CreateGles(name => getProcAddress(name))
                : GRGlInterface.CreateOpenGl(name => getProcAddress(name))
            : GRGlInterface.Create();

        if (glInterface is null)
            throw new ZfPlatformException("Could not create GRGlInterface.");

        using (glInterface)
        {
            GRContext grContext = GRContext.CreateGl(glInterface)
                ?? throw new ZfPlatformException("Could not create GRContext.");

            grContext.SetResourceCacheLimit(ResourceCacheLimit);

            var surface = new SkiaGlSurface(grContext, target);

            try
            {
                surface.Resize(target.Width, target.Height);
            }
            catch
            {
                // the platform falls back to its buffer: the context mustn't stay
                surface.Dispose();
                throw;
            }

            return surface;
        }
    }
}