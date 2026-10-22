using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Gles.Rendering;

/// <summary>One corner of a quad, as the shader reads it.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Vertex
{
    /// <summary>The position in device pixels, the transform already applied.</summary>
    public float X, Y;

    /// <summary>The coordinates in the shape's own space, its center at 0.</summary>
    public float LocalX, LocalY;

    /// <summary>The color, premultiplied, R in the lowest byte.</summary>
    public uint Color;

    /// <summary>The shape's half-width and half-height, the stroke width (0 — filled) and
    /// the kind, <c>shape + 8 × paint</c>.</summary>
    public float HalfWidth, HalfHeight, Stroke, Kind;

    /// <summary>The corner radii TL, TR, BR, BL; for a shadow, its sigma first.</summary>
    public float R0, R1, R2, R3;

    /// <summary>The paint's parameters: a texture's coordinates, a gradient's position
    /// and row, a layer's saturation, the noise's frequency.</summary>
    public float E0, E1, E2, E3;

    public const int Size = 17 * sizeof(float);
}

/// <summary>A rectangle of whole device pixels, for a scissor and a mask's region.</summary>
internal readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public PixelRect Intersect(PixelRect other)
    {
        int left = Math.Max(X, other.X);
        int top = Math.Max(Y, other.Y);
        int right = Math.Min(Right, other.Right);
        int bottom = Math.Min(Bottom, other.Bottom);

        return right > left && bottom > top
            ? new PixelRect(left, top, right - left, bottom - top)
            : new PixelRect(left, top, 0, 0);
    }

    /// <summary>The pixels a device rectangle covers the centers of — what a clip without
    /// antialiasing keeps, as Skia's non-antialiased clip rounds its edges.</summary>
    public static PixelRect Round(Rectangle rect)
    {
        int left = (int)MathF.Round(rect.X);
        int top = (int)MathF.Round(rect.Y);
        int right = (int)MathF.Round(rect.X + rect.Width);
        int bottom = (int)MathF.Round(rect.Y + rect.Height);

        return new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>The pixels a device rectangle touches at all.</summary>
    public static PixelRect Outer(Rectangle rect)
    {
        int left = (int)MathF.Floor(rect.X);
        int top = (int)MathF.Floor(rect.Y);
        int right = (int)MathF.Ceiling(rect.X + rect.Width);
        int bottom = (int)MathF.Ceiling(rect.Y + rect.Height);

        return new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}

/// <summary>An antialiased clip computed in the shader: a rounded rectangle in device
/// pixels, the corners TL, TR, BR, BL.</summary>
internal readonly record struct ClipShape(float Left, float Top, float Right, float Bottom, float TopLeft, float TopRight, float BottomRight, float BottomLeft)
{
    /// <summary>No clip: a rectangle far beyond any target.</summary>
    public static readonly ClipShape None = new(-1e7f, -1e7f, 1e7f, 1e7f, 0, 0, 0, 0);

    public bool IsNone => this == None;
}

internal enum BlendKind : byte
{
    /// <summary>Premultiplied source over: ONE, ONE_MINUS_SRC_ALPHA.</summary>
    Normal,

    /// <summary>The source replaces what is there: a clear under a clip.</summary>
    Replace,
}

internal enum StencilUse : byte
{
    /// <summary>The stencil is not looked at.</summary>
    None,

    /// <summary>Only where the stencil equals the reference: inside every stencil clip.</summary>
    Test,

    /// <summary>A clip being added: where it equals the reference and the shape covers,
    /// it goes up by one; no color is written.</summary>
    Write,
}

/// <summary>Everything a batch shares: what changes it flushes the batch before.</summary>
internal readonly record struct BatchState(
    uint Image,
    bool ImageRepeat,
    BlendKind Blend,
    PixelRect Scissor,
    ClipShape Clip0,
    ClipShape Clip1,
    StencilUse Stencil,
    int StencilReference);

/// <summary>Where the frame is drawn now: the window's framebuffer or a layer's.</summary>
internal readonly record struct RenderTarget(uint Framebuffer, int Width, int Height, int OriginX, int OriginY)
{
    public PixelRect Bounds => new(OriginX, OriginY, Width, Height);
}