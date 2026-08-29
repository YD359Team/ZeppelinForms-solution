using SkiaSharp;

namespace ZeppelinForms.Windows.Rendering;

internal interface IWin32SkiaSurface : IDisposable
{
    void Resize(int width, int height);
    SKSurface? BeginFrame();
    void EndFrame();
    bool SupportsPartialRedraw { get; }
}