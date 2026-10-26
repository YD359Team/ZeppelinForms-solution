using ZeppelinForms.Drawing;

namespace ZeppelinForms.Windows.Rendering;

/// <summary>What a window draws its frames into and presents them from: a WGL context
/// or a DIB section, with the graphics backend's surface over it.</summary>
internal interface IWin32Surface : IDisposable
{
    /// <summary>The client area is now this size, in pixels.</summary>
    void Resize(int width, int height);

    /// <summary>The graphics for a frame, or null while there is nothing to draw
    /// into yet — before the first resize.</summary>
    Graphics? BeginFrame();

    /// <summary>Finish the frame and put it on the screen.</summary>
    void EndFrame();

    bool SupportsPartialRedraw { get; }
}