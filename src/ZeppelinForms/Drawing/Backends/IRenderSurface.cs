namespace ZeppelinForms.Drawing;

/// <summary>A place a backend draws frames into, made by
/// <see cref="IGraphicsBackend"/> for a platform's target.</summary>
/// <remarks>A frame is <see cref="BeginFrame"/>, the drawing, <see cref="EndFrame"/>;
/// the platform presents what was drawn after it. One surface draws one frame at a time,
/// on the thread that made it.</remarks>
public interface IRenderSurface : IDisposable
{
    /// <summary>Begin a frame: the graphics to draw it with, in device pixels, with no
    /// transform and no clip. What a broken frame left — a save without its restore —
    /// is undone here, not carried into this one.</summary>
    Graphics BeginFrame();

    /// <summary>Finish the frame: everything drawn is in the target, ready for the
    /// platform to present.</summary>
    void EndFrame();
}

/// <summary>A surface drawing into a GL framebuffer. Unlike a buffer of pixels, it
/// outlives a resize of its target: the backend's GPU resources — its caches, its
/// glyph atlases — are kept, only the size changes.</summary>
public interface IGlSurface : IRenderSurface
{
    /// <summary>The framebuffer is now this size, in pixels. The platform calls it
    /// with the context current.</summary>
    void Resize(int width, int height);
}