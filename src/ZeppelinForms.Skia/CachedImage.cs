using SkiaSharp;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Skia;

internal sealed class CachedImage : IDisposable
{
    public SKImage SkImage { get; }
    private System.Runtime.InteropServices.GCHandle _handle;

    public CachedImage(SKImage skImage, System.Runtime.InteropServices.GCHandle handle)
    {
        SkImage = skImage;
        _handle = handle;
    }

    public void Dispose()
    {
        SkImage.Dispose();
        if (_handle.IsAllocated) _handle.Free();
    }
}