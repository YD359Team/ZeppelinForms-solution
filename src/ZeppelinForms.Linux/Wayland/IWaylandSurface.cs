using ZeppelinForms.Drawing;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>What a Wayland window draws its frames into and commits them from: an EGL
/// window surface on the GPU, or shared-memory buffers.</summary>
/// <remarks>The window sets the buffer scale and asks for the frame callback before
/// <see cref="EndFrame"/>: both go with the commit the frame ends in.</remarks>
internal interface IWaylandSurface : IDisposable
{
    /// <summary>A frame may begin now: there is a buffer to draw into.</summary>
    bool CanBeginFrame { get; }

    /// <summary>The graphics for a frame of this size in pixels; null when it can't
    /// begin.</summary>
    Graphics? BeginFrame(int width, int height);

    /// <summary>Finish the frame and commit it to <paramref name="surface"/>: all of it,
    /// every frame is whole.</summary>
    void EndFrame(WlSurface surface, int width, int height);
}