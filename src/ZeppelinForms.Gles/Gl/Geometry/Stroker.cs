using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Gles.Geometry;

internal enum LineCap : byte
{
    Butt,
    Round,
    Square,
}

internal enum LineJoin : byte
{
    Miter,
    Round,
    Bevel,
}

/// <summary>
/// The outline of a stroke: the polylines widened into closed rings that, filled by the
/// non-zero rule, cover what the stroke covers — as Skia's stroker makes them.
/// </summary>
/// <remarks>
/// <para>
/// An open polyline becomes one ring: its left side forward, the end cap, its right side
/// back, the start cap. A closed one becomes two: the left side forward and the right side
/// backward, so the area between them winds once and the inside not at all.
/// </para>
/// <para>
/// At a turn the outer side gets the join — a miter, an arc or a bevel — and the inner
/// side goes through the vertex itself. The inner side then folds over itself in a small
/// loop; its winding stays non-zero, and the rasterizer fills it as part of the stroke.
/// One ring per polyline rather than a quad per segment: overlapping pieces would each
/// add their antialiased fringe, and the joins would come out bolder than the lines.
/// </para>
/// </remarks>
internal sealed class Stroker
{
    /// <summary>Skia's default: a miter longer than four half-widths becomes a bevel.</summary>
    public const float DefaultMiterLimit = 4f;

    private readonly List<Point> _left = [];
    private readonly List<Point> _right = [];

    private float _half;
    private LineCap _cap;
    private LineJoin _join;
    private float _miterLimit;
    private float _tolerance;

    /// <param name="input">The polylines, in the units the width is in.</param>
    /// <param name="output">The rings, added to what it has.</param>
    /// <param name="tolerance">How far the arcs of round joins and caps may stray from
    /// the circle, in the same units.</param>
    public void Stroke(
        Polylines input, float width, LineCap cap, LineJoin join, float miterLimit, float tolerance, Polylines output)
    {
        _half = width / 2f;
        _cap = cap;
        _join = join;
        _miterLimit = miterLimit;
        _tolerance = MathF.Max(tolerance, 1e-4f);

        if (_half <= 0) return;

        foreach (Polylines.Contour contour in input.Contours)
            StrokeContour(input.PointsOf(contour), contour.Closed, output);
    }

    private void StrokeContour(ReadOnlySpan<Point> points, bool closed, Polylines output)
    {
        _left.Clear();
        _right.Clear();

        // a closed contour of two points is a line there and back: stroked as an open one
        if (closed && points.Length < 3) closed = false;

        if (points.Length == 1)
        {
            Dot(points[0], output);
            return;
        }

        int n = points.Length;

        if (closed)
        {
            // every vertex is a join, the first one between the closing segment and the first
            for (int i = 0; i < n; i++)
                Join(points[(i + n - 1) % n], points[i], points[(i + 1) % n]);

            output.BeginContour();
            foreach (Point p in _left) output.Add(p);
            output.EndContour(closed: true);

            // backward: the hole winds against the outside and stays empty
            output.BeginContour();
            for (int i = _right.Count - 1; i >= 0; i--) output.Add(_right[i]);
            output.EndContour(closed: true);

            return;
        }

        Point startNormal = Normal(points[0], points[1]);
        _left.Add(Offset(points[0], startNormal, 1));
        _right.Add(Offset(points[0], startNormal, -1));

        for (int i = 1; i < n - 1; i++)
            Join(points[i - 1], points[i], points[i + 1]);

        Point endNormal = Normal(points[n - 2], points[n - 1]);
        _left.Add(Offset(points[n - 1], endNormal, 1));
        _right.Add(Offset(points[n - 1], endNormal, -1));

        output.BeginContour();

        foreach (Point p in _left) output.Add(p);

        Cap(points[n - 1], Direction(points[n - 2], points[n - 1]), output);

        for (int i = _right.Count - 1; i >= 0; i--) output.Add(_right[i]);

        Cap(points[0], Direction(points[1], points[0]), output);

        output.EndContour(closed: true);
    }

    /// <summary>The join at <paramref name="b"/> between a→b and b→c, onto both sides.</summary>
    private void Join(Point a, Point b, Point c)
    {
        Point n0 = Normal(a, b);
        Point n1 = Normal(b, c);

        Point d0 = Direction(a, b);
        Point d1 = Direction(b, c);
        float cross = d0.X * d1.Y - d0.Y * d1.X;
        float dot = d0.X * d1.X + d0.Y * d1.Y;

        // straight on, or nearly: one point a side, no join
        if (MathF.Abs(cross) < 1e-5f && dot > 0)
        {
            _left.Add(Offset(b, n0, 1));
            _right.Add(Offset(b, n0, -1));
            return;
        }

        // the left side is the +normal one; a turn to the right on the screen (cross > 0,
        // y down) has its outer corner on the left
        bool outerIsLeft = cross > 0;

        List<Point> outer = outerIsLeft ? _left : _right;
        List<Point> inner = outerIsLeft ? _right : _left;
        float sign = outerIsLeft ? 1 : -1;

        Point outerFrom = Offset(b, n0, sign);
        Point outerTo = Offset(b, n1, sign);

        // the inner side through the vertex: its loop stays inside the stroke
        inner.Add(Offset(b, n0, -sign));
        inner.Add(b);
        inner.Add(Offset(b, n1, -sign));

        outer.Add(outerFrom);

        switch (_join)
        {
            case LineJoin.Round:
                ArcAround(b, outerFrom, outerTo, outer);
                break;

            case LineJoin.Miter:
                {
                    // the tip along the bisector of the normals, 1/cos(θ/2) half-widths out
                    float mx = n0.X + n1.X, my = n0.Y + n1.Y;
                    float length = MathF.Sqrt(mx * mx + my * my);

                    if (length > 1e-6f)
                    {
                        float cosHalf = length / 2f;
                        float ratio = 1f / cosHalf;

                        if (ratio <= _miterLimit)
                        {
                            float reach = _half * ratio / length;
                            outer.Add(new Point(b.X + mx * reach * sign, b.Y + my * reach * sign));
                        }
                    }

                    break;
                }
        }

        outer.Add(outerTo);
    }

    /// <summary>The arc of a round join from one offset point to the other, the short way
    /// around the vertex.</summary>
    private void ArcAround(Point center, Point from, Point to, List<Point> output)
    {
        float a0 = MathF.Atan2(from.Y - center.Y, from.X - center.X);
        float a1 = MathF.Atan2(to.Y - center.Y, to.X - center.X);

        float sweep = a1 - a0;
        while (sweep > MathF.PI) sweep -= 2 * MathF.PI;
        while (sweep < -MathF.PI) sweep += 2 * MathF.PI;

        int steps = StepsFor(MathF.Abs(sweep));

        for (int i = 1; i < steps; i++)
        {
            float a = a0 + sweep * i / steps;
            output.Add(new Point(center.X + _half * MathF.Cos(a), center.Y + _half * MathF.Sin(a)));
        }
    }

    /// <summary>The cap at an end, going from the left side to the right one: the direction
    /// points out of the line.</summary>
    private void Cap(Point end, Point direction, Polylines output)
    {
        // the outline arrives on the +normal side of the outgoing direction and leaves on
        // the -normal one, around the end
        var normal = new Point(direction.Y, -direction.X);

        switch (_cap)
        {
            case LineCap.Round:
                {
                    float a0 = MathF.Atan2(normal.Y, normal.X);
                    int steps = StepsFor(MathF.PI);

                    for (int i = 1; i < steps; i++)
                    {
                        float a = a0 + MathF.PI * i / steps;
                        output.Add(new Point(end.X + _half * MathF.Cos(a), end.Y + _half * MathF.Sin(a)));
                    }

                    break;
                }

            case LineCap.Square:
                output.Add(new Point(
                    end.X + (normal.X + direction.X) * _half,
                    end.Y + (normal.Y + direction.Y) * _half));
                output.Add(new Point(
                    end.X + (-normal.X + direction.X) * _half,
                    end.Y + (-normal.Y + direction.Y) * _half));
                break;
        }
    }

    /// <summary>A polyline of no length: a dot by a round cap, a square by a square one,
    /// nothing by a butt — as Skia draws a zero-length line.</summary>
    private void Dot(Point at, Polylines output)
    {
        switch (_cap)
        {
            case LineCap.Round:
                {
                    int steps = StepsFor(2 * MathF.PI);

                    output.BeginContour();

                    for (int i = 0; i < steps; i++)
                    {
                        float a = 2 * MathF.PI * i / steps;
                        output.Add(new Point(at.X + _half * MathF.Cos(a), at.Y + _half * MathF.Sin(a)));
                    }

                    output.EndContour(closed: true);
                    break;
                }

            case LineCap.Square:
                output.BeginContour();
                output.Add(new Point(at.X - _half, at.Y - _half));
                output.Add(new Point(at.X + _half, at.Y - _half));
                output.Add(new Point(at.X + _half, at.Y + _half));
                output.Add(new Point(at.X - _half, at.Y + _half));
                output.EndContour(closed: true);
                break;
        }
    }

    /// <summary>The steps an arc of the half-width needs to keep within the tolerance.</summary>
    private int StepsFor(float sweep)
    {
        float ratio = 1f - _tolerance / _half;
        float stepAngle = ratio <= 0 ? MathF.PI / 2f : 2f * MathF.Acos(ratio);

        return Math.Clamp((int)MathF.Ceiling(sweep / MathF.Max(stepAngle, 1e-3f)), 1, 256);
    }

    private Point Offset(Point p, Point normal, float sign) =>
        new(p.X + normal.X * _half * sign, p.Y + normal.Y * _half * sign);

    private static Point Direction(Point a, Point b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);

        return length > 0 ? new Point(dx / length, dy / length) : new Point(1, 0);
    }

    /// <summary>The unit normal of a→b: the direction turned a quarter counter-clockwise on
    /// the screen, to the left of a walk from a to b.</summary>
    private static Point Normal(Point a, Point b)
    {
        Point d = Direction(a, b);
        return new Point(d.Y, -d.X);
    }
}