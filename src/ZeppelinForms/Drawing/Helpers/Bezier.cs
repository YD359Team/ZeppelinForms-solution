using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing.Helpers;

/// <summary>Cubic Bézier curves for the backends that have none of their own: the
/// check of a chain's points and its flattening into a polyline.</summary>
internal static class Bezier
{
    /// <summary>How far a flattened segment may stray from the curve, in units of the
    /// canvas. A quarter: at a scale of 2 that is half a device pixel, and the
    /// segments of a stroke can't be told from the curve.</summary>
    private const float Tolerance = 0.25f;

    /// <summary>Above this a curve is split no further — a control point thrown far
    /// away by an error mustn't cost a million points.</summary>
    private const int MaxSegments = 512;

    /// <summary>The number of curves in a chain of points: the first point, then three
    /// for each curve. An empty chain has none; any other count than 1 + 3n is an
    /// error of the caller, as in WinForms' <c>DrawBeziers</c>.</summary>
    public static int CountCurves(ReadOnlySpan<Point> points, string paramName)
    {
        if (points.Length == 0) return 0;

        if (points.Length < 4 || (points.Length - 1) % 3 != 0)
            throw new ArgumentException(
                $"A chain of Bézier curves takes 1 + 3n points: the start, then two control points and an end for each curve. {points.Length} were given.",
                paramName);

        return (points.Length - 1) / 3;
    }

    /// <summary>The number of straight segments that keep within <see cref="Tolerance"/>
    /// of the curve — Wang's formula: it bounds the curve's second derivative by the
    /// second differences of its control points.</summary>
    public static int Segments(Point p0, Point p1, Point p2, Point p3)
    {
        float ax = p0.X - 2f * p1.X + p2.X;
        float ay = p0.Y - 2f * p1.Y + p2.Y;
        float bx = p1.X - 2f * p2.X + p3.X;
        float by = p1.Y - 2f * p2.Y + p3.Y;

        float m = MathF.Sqrt(MathF.Max(ax * ax + ay * ay, bx * bx + by * by));

        // n(n-1)/8 for a cubic — 3·2/8
        float n = MathF.Ceiling(MathF.Sqrt(0.75f * m / Tolerance));

        // NaN from a point at infinity goes to the cap, not to zero
        if (!(n >= 1f)) return float.IsNaN(n) ? MaxSegments : 1;

        return (int)MathF.Min(n, MaxSegments);
    }

    public static Point Evaluate(Point p0, Point p1, Point p2, Point p3, float t)
    {
        float u = 1f - t;
        float a = u * u * u;
        float b = 3f * u * u * t;
        float c = 3f * u * t * t;
        float d = t * t * t;

        return new Point(
            a * p0.X + b * p1.X + c * p2.X + d * p3.X,
            a * p0.Y + b * p1.Y + c * p2.Y + d * p3.Y);
    }

    /// <summary>The number of points the flattened chain will have — for the caller
    /// to rent the buffer once.</summary>
    public static int FlattenedLength(ReadOnlySpan<Point> points, int curves)
    {
        int total = 1;

        for (int i = 0; i < curves; i++)
        {
            int k = i * 3;
            total += Segments(points[k], points[k + 1], points[k + 2], points[k + 3]);
        }

        return total;
    }

    /// <summary>Flatten a chain into <paramref name="destination"/>, sized by
    /// <see cref="FlattenedLength"/>. The ends of the curves are copied as they are,
    /// not computed: the joints of a chain meet exactly.</summary>
    public static int Flatten(ReadOnlySpan<Point> points, int curves, Span<Point> destination)
    {
        int n = 0;
        destination[n++] = points[0];

        for (int i = 0; i < curves; i++)
        {
            int k = i * 3;
            Point p0 = points[k], p1 = points[k + 1], p2 = points[k + 2], p3 = points[k + 3];
            int segments = Segments(p0, p1, p2, p3);

            for (int s = 1; s < segments; s++)
                destination[n++] = Evaluate(p0, p1, p2, p3, s / (float)segments);

            destination[n++] = p3;
        }

        return n;
    }
}