using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Gles.Geometry;

/// <summary>
/// A 2D affine transform: x' = A·x + C·y + E, y' = B·x + D·y + F — what Skia's matrix is
/// without perspective, and all <see cref="Drawing.Graphics"/> can make.
/// </summary>
/// <remarks>The vertices are transformed on the CPU: a batch then mixes primitives drawn
/// under different transforms, and the GPU sees device pixels only.</remarks>
internal readonly record struct Affine(float A, float B, float C, float D, float E, float F)
{
    public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

    public static Affine Translation(float dx, float dy) => new(1, 0, 0, 1, dx, dy);

    public static Affine Scaling(float sx, float sy) => new(sx, 0, 0, sy, 0, 0);

    /// <summary>Degrees clockwise on the screen, as Skia's RotateDegrees: y goes down.</summary>
    public static Affine Rotation(float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);

        return new Affine(cos, sin, -sin, cos, 0, 0);
    }

    /// <summary>Skia's skew: x' = x + sx·y, y' = sy·x + y.</summary>
    public static Affine Skewing(float sx, float sy) => new(1, sy, sx, 1, 0, 0);

    /// <summary>This transform after <paramref name="inner"/>: what the canvas's concat
    /// makes — the new transform applies to the coordinates first.</summary>
    public Affine Then(in Affine inner) => new(
        A * inner.A + C * inner.B,
        B * inner.A + D * inner.B,
        A * inner.C + C * inner.D,
        B * inner.C + D * inner.D,
        A * inner.E + C * inner.F + E,
        B * inner.E + D * inner.F + F);

    public Point Map(Point p) => new(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);

    public Point Map(float x, float y) => new(A * x + C * y + E, B * x + D * y + F);

    /// <summary>Only a scale and an offset: an axis-aligned rectangle stays one.</summary>
    public bool IsAxisAligned => B == 0 && C == 0;

    /// <summary>A scale and an offset, the scale positive: a rectangle maps onto one with
    /// its corners in the same places — what a scissor and an analytic clip need.</summary>
    public bool IsPositiveScaleTranslate => IsAxisAligned && A > 0 && D > 0;

    /// <summary>The smaller of the axes' scales: a device pixel is at most 1/this local
    /// units long, whichever way it goes.</summary>
    public float MinScale
    {
        get
        {
            float sx = MathF.Sqrt(A * A + B * B);
            float sy = MathF.Sqrt(C * C + D * D);
            return MathF.Max(1e-6f, MathF.Min(sx, sy));
        }
    }

    /// <summary>The average scale: how many device pixels a local unit is, for flattening
    /// curves to a tolerance in pixels.</summary>
    public float MeanScale => MathF.Max(1e-6f, MathF.Sqrt(MathF.Abs(A * D - B * C)));

    /// <summary>The device bounds of a local rectangle.</summary>
    public Rectangle MapBounds(Rectangle rect)
    {
        Point p0 = Map(rect.X, rect.Y);
        Point p1 = Map(rect.X + rect.Width, rect.Y);
        Point p2 = Map(rect.X + rect.Width, rect.Y + rect.Height);
        Point p3 = Map(rect.X, rect.Y + rect.Height);

        float left = MathF.Min(MathF.Min(p0.X, p1.X), MathF.Min(p2.X, p3.X));
        float top = MathF.Min(MathF.Min(p0.Y, p1.Y), MathF.Min(p2.Y, p3.Y));
        float right = MathF.Max(MathF.Max(p0.X, p1.X), MathF.Max(p2.X, p3.X));
        float bottom = MathF.Max(MathF.Max(p0.Y, p1.Y), MathF.Max(p2.Y, p3.Y));

        return new Rectangle(left, top, right - left, bottom - top);
    }
}