using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Linux;

/// <summary>What a window draws its frames into and presents them from: an EGL window
/// surface on the GPU, or an XImage in memory, with the graphics backend's surface
/// over it.</summary>
internal interface IX11Surface : IDisposable
{
    /// <summary>The window is now this size, in pixels.</summary>
    void Resize(int width, int height);

    /// <summary>The graphics for a frame, or null while there is nothing to draw
    /// into yet — before the first resize.</summary>
    Graphics? BeginFrame();

    /// <summary>Finish the frame and put it on the screen: all of it, or only the
    /// <paramref name="dirty"/> part of it (in pixels) where the surface can.</summary>
    void EndFrame(Rectangle? dirty = null);

    /// <summary>A frame may redraw only a part: what wasn't redrawn is still there.
    /// Not so after a swap, which leaves the back buffer undefined.</summary>
    bool SupportsPartialRedraw { get; }
}