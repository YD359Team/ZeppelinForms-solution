using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing;

/// <summary>
/// A snapshot of an element's content taken bypassing the canvas. The effect draws
/// it itself — as many times and in whatever way it needs.
/// </summary>
/// <remarks>Owns a native image, so it is released explicitly: waiting for the
/// finalizer here would mean holding a frame surface.</remarks>
public abstract class LayerCapture : IDisposable
{
    /// <summary>The area the capture was taken from, in canvas coordinates.</summary>
    public abstract Rectangle Bounds { get; }

    public abstract void Dispose();
}

/// <summary>Which channels to let through when drawing a capture.</summary>
public enum ColorChannels
{
    All,
    Red,
    Green,
    Blue,

    /// <summary>Green and blue — the complement to red.</summary>
    Cyan,
    Magenta,
    Yellow,
}

/// <summary>How a capture blends with what is already drawn.</summary>
public enum CaptureBlend
{
    Normal,

    /// <summary>Lightening: the two halves of the separated channels add back
    /// up to the original color where they coincide.</summary>
    Screen,
}