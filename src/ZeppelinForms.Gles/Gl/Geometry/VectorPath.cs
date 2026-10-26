using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Gles.Geometry;

/// <summary>
/// A path of lines and cubic Béziers in local units: what an SVG path, an arc, a pie and a
/// polygon become before they are flattened into polylines and rasterized.
/// </summary>
/// <remarks>Quadratic curves and elliptical arcs are kept as cubics: a quadratic is one
/// exactly, an arc of up to a quarter turn is one within 0.03 % of its radius.</remarks>
internal sealed class VectorPath
{
    private enum Verb : byte
    {
        Move,
        Line,
        Cubic,
        Close,
    }

    private readonly List<Verb> _verbs = [];
    private readonly List<Point> _points = [];

    private Point _start;
    private Point _current;
    private bool _open;

    public bool IsEmpty => _verbs.Count == 0;

    public Point Current => _current;

    public void Clear()
    {
        _verbs.Clear();
        _points.Clear();
        _open = false;
        _current = _start = default;
    }

    public void MoveTo(Point p)
    {
        _verbs.Add(Verb.Move);
        _points.Add(p);
        _start = _current = p;
        _open = true;
    }

    public void LineTo(Point p)
    {
        EnsureOpen();

        _verbs.Add(Verb.Line);
        _points.Add(p);
        _current = p;
    }

    public void CubicTo(Point c1, Point c2, Point p)
    {
        EnsureOpen();

        _verbs.Add(Verb.Cubic);
        _points.Add(c1);
        _points.Add(c2);
        _points.Add(p);
        _current = p;
    }

    /// <summary>A quadratic Bézier, raised to the cubic it is.</summary>
    public void QuadTo(Point c, Point p)
    {
        Point from = _current;

        CubicTo(
            new Point(from.X + (c.X - from.X) * 2f / 3f, from.Y + (c.Y - from.Y) * 2f / 3f),
            new Point(p.X + (c.X - p.X) * 2f / 3f, p.Y + (c.Y - p.Y) * 2f / 3f),
            p);
    }

    public void Close()
    {
        if (!_open) return;

        _verbs.Add(Verb.Close);
        _current = _start;
        _open = false;
    }

    /// <summary>A segment without a move before it starts at the last point, as in SVG:
    /// after a close, from the subpath's start.</summary>
    private void EnsureOpen()
    {
        if (_open) return;

        _verbs.Add(Verb.Move);
        _points.Add(_current);
        _start = _current;
        _open = true;
    }

    /// <summary>Another path's subpaths added to this one, mapped by a transform: a composite
    /// glyph made of its components.</summary>
    public void Append(VectorPath other, in Affine matrix)
    {
        ArgumentNullException.ThrowIfNull(other);

        int p = 0;

        foreach (Verb verb in other._verbs)
        {
            switch (verb)
            {
                case Verb.Move:
                    MoveTo(matrix.Map(other._points[p++]));
                    break;

                case Verb.Line:
                    LineTo(matrix.Map(other._points[p++]));
                    break;

                case Verb.Cubic:
                    CubicTo(matrix.Map(other._points[p]), matrix.Map(other._points[p + 1]), matrix.Map(other._points[p + 2]));
                    p += 3;
                    break;

                case Verb.Close:
                    Close();
                    break;
            }
        }
    }

    /// <summary>An arc of an axis-aligned ellipse by its center: angles in radians, clockwise
    /// on the screen from the x axis. Joined to the path by a line, or begins a subpath.</summary>
    public void AddArc(Point center, float rx, float ry, float start, float sweep, bool moveTo)
    {
        Point first = new(center.X + rx * MathF.Cos(start), center.Y + ry * MathF.Sin(start));

        if (moveTo) MoveTo(first);
        else LineTo(first);

        // quarter turns at most: the cubic of a longer arc drifts off the circle
        int segments = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(sweep) / (MathF.PI / 2f) - 1e-4f));
        float step = sweep / segments;
        float k = 4f / 3f * MathF.Tan(step / 4f);

        float angle = start;

        for (int i = 0; i < segments; i++)
        {
            float a0 = angle;
            float a1 = angle + step;

            float cos0 = MathF.Cos(a0), sin0 = MathF.Sin(a0);
            float cos1 = MathF.Cos(a1), sin1 = MathF.Sin(a1);

            CubicTo(
                new Point(center.X + rx * (cos0 - k * sin0), center.Y + ry * (sin0 + k * cos0)),
                new Point(center.X + rx * (cos1 + k * sin1), center.Y + ry * (sin1 - k * cos1)),
                new Point(center.X + rx * cos1, center.Y + ry * sin1));

            angle = a1;
        }
    }

    /// <summary>SVG's elliptical arc from the current point to <paramref name="to"/>, by the
    /// endpoint parameterization of SVG 1.1 F.6.5 — radii too small for the endpoints are
    /// scaled up, as the specification says.</summary>
    public void SvgArcTo(float rx, float ry, float xAxisRotation, bool largeArc, bool sweep, Point to)
    {
        Point from = _current;

        if (from == to) return;

        rx = MathF.Abs(rx);
        ry = MathF.Abs(ry);

        if (rx == 0 || ry == 0)
        {
            LineTo(to);
            return;
        }

        float phi = xAxisRotation * MathF.PI / 180f;
        float cosPhi = MathF.Cos(phi);
        float sinPhi = MathF.Sin(phi);

        float dx2 = (from.X - to.X) / 2f;
        float dy2 = (from.Y - to.Y) / 2f;

        float x1p = cosPhi * dx2 + sinPhi * dy2;
        float y1p = -sinPhi * dx2 + cosPhi * dy2;

        float lambda = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);

        if (lambda > 1)
        {
            float s = MathF.Sqrt(lambda);
            rx *= s;
            ry *= s;
        }

        float rx2 = rx * rx, ry2 = ry * ry;
        float numerator = rx2 * ry2 - rx2 * y1p * y1p - ry2 * x1p * x1p;
        float denominator = rx2 * y1p * y1p + ry2 * x1p * x1p;

        float coef = denominator == 0 ? 0 : MathF.Sqrt(MathF.Max(0, numerator / denominator));
        if (largeArc == sweep) coef = -coef;

        float cxp = coef * rx * y1p / ry;
        float cyp = -coef * ry * x1p / rx;

        float cx = cosPhi * cxp - sinPhi * cyp + (from.X + to.X) / 2f;
        float cy = sinPhi * cxp + cosPhi * cyp + (from.Y + to.Y) / 2f;

        float theta1 = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        float delta = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);

        if (!sweep && delta > 0) delta -= 2 * MathF.PI;
        else if (sweep && delta < 0) delta += 2 * MathF.PI;

        int segments = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(delta) / (MathF.PI / 2f) - 1e-4f));
        float step = delta / segments;
        float k = 4f / 3f * MathF.Tan(step / 4f);

        float angle = theta1;

        for (int i = 0; i < segments; i++)
        {
            float a1 = angle + step;

            float cos0 = MathF.Cos(angle), sin0 = MathF.Sin(angle);
            float cos1 = MathF.Cos(a1), sin1 = MathF.Sin(a1);

            // on the unit circle, then scaled, rotated and moved onto the ellipse
            Point c1 = Place(cos0 - k * sin0, sin0 + k * cos0);
            Point c2 = Place(cos1 + k * sin1, sin1 - k * cos1);
            Point end = i == segments - 1 ? to : Place(cos1, sin1);

            CubicTo(c1, c2, end);

            angle = a1;
        }

        Point Place(float ux, float uy)
        {
            float x = ux * rx;
            float y = uy * ry;

            return new Point(cosPhi * x - sinPhi * y + cx, sinPhi * x + cosPhi * y + cy);
        }

        static float Angle(float ux, float uy, float vx, float vy)
        {
            float dot = ux * vx + uy * vy;
            float length = MathF.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));

            float angle = MathF.Acos(Math.Clamp(dot / length, -1f, 1f));

            return ux * vy - uy * vx < 0 ? -angle : angle;
        }
    }

    /// <summary>The bounds of all the points, the control points of the curves included:
    /// what Skia's <c>SKPath.Bounds</c> is, and what an SVG icon is fitted by.</summary>
    public Rectangle ControlBounds
    {
        get
        {
            if (_points.Count == 0) return Rectangle.Empty;

            float left = float.MaxValue, top = float.MaxValue;
            float right = float.MinValue, bottom = float.MinValue;

            foreach (Point p in _points)
            {
                left = MathF.Min(left, p.X);
                top = MathF.Min(top, p.Y);
                right = MathF.Max(right, p.X);
                bottom = MathF.Max(bottom, p.Y);
            }

            return new Rectangle(left, top, right - left, bottom - top);
        }
    }

    /// <summary>The path as polylines, mapped by <paramref name="matrix"/> first: the curves
    /// are split until they are within <paramref name="tolerance"/> of the polyline, in the
    /// units after the mapping.</summary>
    public void Flatten(in Affine matrix, float tolerance, Polylines output)
    {
        int p = 0;
        bool inContour = false;

        // a move starts a contour only once something is drawn from it: a bare move
        // draws nothing — in SVG and in Skia — and the stroker would have made a dot of
        // it, as of a line of no length. Icons start with "M0 0M24 24" to set their grid,
        // and those two moves came out as dots in the corners
        Point? pending = null;

        void Start()
        {
            if (pending is not { } start) return;

            if (inContour) output.EndContour(closed: false);

            output.BeginContour();
            output.Add(start);
            inContour = true;
            pending = null;
        }

        for (int v = 0; v < _verbs.Count; v++)
        {
            switch (_verbs[v])
            {
                case Verb.Move:
                    pending = matrix.Map(_points[p++]);
                    break;

                case Verb.Line:
                    Start();
                    output.Add(matrix.Map(_points[p++]));
                    break;

                case Verb.Cubic:
                    {
                        Start();

                        Point p0 = output.Last;
                        Point p1 = matrix.Map(_points[p]);
                        Point p2 = matrix.Map(_points[p + 1]);
                        Point p3 = matrix.Map(_points[p + 2]);
                        p += 3;

                        FlattenCubic(p0, p1, p2, p3, tolerance, output);
                        break;
                    }

                case Verb.Close:
                    // a move closed at once is drawn: Skia caps it as a line of no length
                    Start();

                    if (inContour) output.EndContour(closed: true);
                    inContour = false;
                    break;
            }
        }

        if (inContour) output.EndContour(closed: false);
    }

    /// <summary>A cubic as a polyline of equal steps in t, as many as Wang's formula says:
    /// the second differences of the control points bound how far the curve strays from
    /// its chords.</summary>
    public static void FlattenCubic(Point p0, Point p1, Point p2, Point p3, float tolerance, Polylines output)
    {
        float ddx = MathF.Max(MathF.Abs(p0.X - 2 * p1.X + p2.X), MathF.Abs(p1.X - 2 * p2.X + p3.X));
        float ddy = MathF.Max(MathF.Abs(p0.Y - 2 * p1.Y + p2.Y), MathF.Abs(p1.Y - 2 * p2.Y + p3.Y));
        float dd = MathF.Sqrt(ddx * ddx + ddy * ddy);

        int n = (int)MathF.Ceiling(MathF.Sqrt(0.75f * dd / MathF.Max(tolerance, 1e-4f)));
        n = Math.Clamp(n, 1, 512);

        for (int i = 1; i <= n; i++)
        {
            float t = (float)i / n;
            float mt = 1 - t;

            float a = mt * mt * mt;
            float b = 3 * mt * mt * t;
            float c = 3 * mt * t * t;
            float d = t * t * t;

            output.Add(new Point(
                a * p0.X + b * p1.X + c * p2.X + d * p3.X,
                a * p0.Y + b * p1.Y + c * p2.Y + d * p3.Y));
        }
    }
}

/// <summary>Contours of points, each open or closed: a flattened path, a stroke's outline.
/// The buffers are kept and reused, so a frame of icons allocates nothing.</summary>
internal sealed class Polylines
{
    private Point[] _points = new Point[256];
    private readonly List<Contour> _contours = [];

    private int _count;
    private int _contourStart = -1;

    public readonly record struct Contour(int Start, int Count, bool Closed);

    public int PointCount => _count;

    public IReadOnlyList<Contour> Contours => _contours;

    public ReadOnlySpan<Point> Points => _points.AsSpan(0, _count);

    public ReadOnlySpan<Point> PointsOf(Contour contour) => _points.AsSpan(contour.Start, contour.Count);

    public Point Last => _count > 0 ? _points[_count - 1] : default;

    public void Clear()
    {
        _count = 0;
        _contours.Clear();
        _contourStart = -1;
    }

    public void BeginContour()
    {
        if (_contourStart >= 0) EndContour(closed: false);

        _contourStart = _count;
    }

    public void Add(Point p)
    {
        if (_contourStart < 0) _contourStart = _count;

        // a point on the last one adds nothing and gives the stroker a zero-length segment
        if (_count > _contourStart && _points[_count - 1] == p) return;

        if (_count == _points.Length) Array.Resize(ref _points, _points.Length * 2);

        _points[_count++] = p;
    }

    public void EndContour(bool closed)
    {
        if (_contourStart < 0) return;

        int count = _count - _contourStart;

        // closed, the last point on the first is the closing segment already
        if (closed && count > 1 && _points[_count - 1] == _points[_contourStart])
        {
            _count--;
            count--;
        }

        if (count > 0) _contours.Add(new Contour(_contourStart, count, closed));
        else _count = _contourStart;

        _contourStart = -1;
    }

    /// <summary>Map every point by a transform, in place.</summary>
    public void Transform(in Affine matrix)
    {
        for (int i = 0; i < _count; i++)
            _points[i] = matrix.Map(_points[i]);
    }

    public Rectangle Bounds
    {
        get
        {
            if (_count == 0) return Rectangle.Empty;

            float left = float.MaxValue, top = float.MaxValue;
            float right = float.MinValue, bottom = float.MinValue;

            for (int i = 0; i < _count; i++)
            {
                Point p = _points[i];
                left = MathF.Min(left, p.X);
                top = MathF.Min(top, p.Y);
                right = MathF.Max(right, p.X);
                bottom = MathF.Max(bottom, p.Y);
            }

            return new Rectangle(left, top, right - left, bottom - top);
        }
    }
}