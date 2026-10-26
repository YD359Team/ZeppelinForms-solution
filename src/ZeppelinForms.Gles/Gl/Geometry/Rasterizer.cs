using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Gles.Geometry;

internal enum FillRule : byte
{
    NonZero,
    EvenOdd,
}

/// <summary>
/// Antialiased coverage of polygons on the CPU, by exact area: every edge adds the signed
/// area it sweeps to the cells it crosses, and a running sum along each row gives the
/// winding, fractional at the edges — the accumulation rasterizer of font-rs.
/// </summary>
/// <remarks>
/// <para>
/// The GPU has no antialiased path fill of its own without multisampling, and a window's
/// framebuffer rarely has that. Paths in a form are small — icons, check marks, chart
/// lines, pies — and a mask of them costs less on the CPU than any stencil trick costs in
/// round trips; the mask then goes up into an atlas and is drawn as a quad.
/// </para>
/// <para>
/// A region of the device is rasterized at a time: a path larger than the atlas is drawn in
/// tiles, and a path mostly outside the clip costs only the part inside it. Edges are cut
/// to the region: what lies above or below adds nothing, what lies left is moved onto the
/// region's left edge, where it still winds the cells right of it.
/// </para>
/// </remarks>
internal sealed class Rasterizer
{
    private float[] _cells = [];

    private int _width;
    private int _height;
    private int _stride;
    private float _originX;
    private float _originY;

    /// <summary>Coverage of the closed contours in a device region, one byte a pixel, row
    /// by row from the top.</summary>
    public void Rasterize(Polylines contours, int x, int y, int width, int height, FillRule rule, Span<byte> coverage)
    {
        _width = width;
        _height = height;
        _stride = width + 2;
        _originX = x;
        _originY = y;

        int size = _stride * height;

        if (_cells.Length < size) _cells = new float[Math.Max(size, _cells.Length * 2)];

        Array.Clear(_cells, 0, size);

        foreach (Polylines.Contour contour in contours.Contours)
        {
            ReadOnlySpan<Point> points = contours.PointsOf(contour);
            if (points.Length < 2) continue;

            // every contour is filled closed, whether it was drawn closed or not
            for (int i = 0; i < points.Length; i++)
            {
                Point a = points[i];
                Point b = points[(i + 1) % points.Length];

                ClippedLine(a.X - _originX, a.Y - _originY, b.X - _originX, b.Y - _originY);
            }
        }

        Accumulate(rule, coverage);
    }

    /// <summary>An edge cut at the left and right of the region: the part beyond the left
    /// edge runs down it instead, the part beyond the right one is dropped.</summary>
    private void ClippedLine(float x0, float y0, float x1, float y1)
    {
        if (y0 == y1) return;

        // wholly above or below: nothing in the region
        if ((y0 <= 0 && y1 <= 0) || (y0 >= _height && y1 >= _height)) return;

        float left = 0, right = _width;

        // wholly right: no cell of the region is right of it
        if (x0 >= right && x1 >= right) return;

        if (x0 <= left && x1 <= left)
        {
            Line(left, y0, left, y1);
            return;
        }

        // split where it crosses the left edge and the right edge
        float dxdy = (x1 - x0) / (y1 - y0);

        Span<float> ys = stackalloc float[4];
        int count = 0;

        ys[count++] = y0;

        if ((x0 < left) != (x1 < left)) ys[count++] = y0 + (left - x0) / dxdy;
        if ((x0 > right) != (x1 > right)) ys[count++] = y0 + (right - x0) / dxdy;

        ys[count++] = y1;

        // in order along the edge
        if (count == 4 && (ys[1] - y0) * (y1 - y0) > (ys[2] - y0) * (y1 - y0))
            (ys[1], ys[2]) = (ys[2], ys[1]);

        for (int i = 0; i < count - 1; i++)
        {
            float ya = ys[i], yb = ys[i + 1];
            if (ya == yb) continue;

            float xa = x0 + (ya - y0) * dxdy;
            float xb = x0 + (yb - y0) * dxdy;

            float mid = (xa + xb) / 2f;

            if (mid >= right) continue;

            if (mid <= left)
            {
                Line(left, ya, left, yb);
                continue;
            }

            Line(Math.Clamp(xa, left, right), ya, Math.Clamp(xb, left, right), yb);
        }
    }

    /// <summary>The signed area an edge inside the region sweeps, into the cells it crosses.</summary>
    private void Line(float x0, float y0, float x1, float y1)
    {
        if (y0 == y1) return;

        float direction;

        if (y0 < y1)
        {
            direction = 1f;
        }
        else
        {
            direction = -1f;
            (x0, x1) = (x1, x0);
            (y0, y1) = (y1, y0);
        }

        float dxdy = (x1 - x0) / (y1 - y0);
        float x = x0;

        if (y0 < 0)
        {
            x -= y0 * dxdy;
            y0 = 0;
        }

        if (y1 > _height) y1 = _height;
        if (y0 >= y1) return;

        int yStart = (int)y0;
        int yEnd = (int)MathF.Ceiling(y1);

        for (int y = yStart; y < yEnd; y++)
        {
            int row = y * _stride;

            float dy = MathF.Min(y + 1, y1) - MathF.Max(y, y0);
            float xNext = x + dxdy * dy;
            float d = dy * direction;

            float xa = x < xNext ? x : xNext;
            float xb = x < xNext ? xNext : x;

            float xaFloor = MathF.Floor(xa);
            int xaInt = (int)xaFloor;
            float xbCeil = MathF.Ceiling(xb);
            int xbInt = (int)xbCeil;

            if (xbInt <= xaInt + 1)
            {
                // within one cell: split at the mean x of the edge in it
                float xmf = 0.5f * (x + xNext) - xaFloor;

                _cells[row + xaInt] += d - d * xmf;
                _cells[row + xaInt + 1] += d * xmf;
            }
            else
            {
                float s = 1f / (xb - xa);
                float xaFrac = xa - xaFloor;
                float a0 = 0.5f * s * (1 - xaFrac) * (1 - xaFrac);
                float xbFrac = xb - xbCeil + 1;
                float am = 0.5f * s * xbFrac * xbFrac;

                _cells[row + xaInt] += d * a0;

                if (xbInt == xaInt + 2)
                {
                    _cells[row + xaInt + 1] += d * (1 - a0 - am);
                }
                else
                {
                    float a1 = s * (1.5f - xaFrac);
                    _cells[row + xaInt + 1] += d * (a1 - a0);

                    for (int xi = xaInt + 2; xi < xbInt - 1; xi++)
                        _cells[row + xi] += d * s;

                    float a2 = a1 + (xbInt - xaInt - 3) * s;
                    _cells[row + xbInt - 1] += d * (1 - a2 - am);
                }

                _cells[row + xbInt] += d * am;
            }

            x = xNext;
        }
    }

    private void Accumulate(FillRule rule, Span<byte> coverage)
    {
        for (int y = 0; y < _height; y++)
        {
            int row = y * _stride;
            int output = y * _width;
            float sum = 0;

            for (int x = 0; x < _width; x++)
            {
                sum += _cells[row + x];

                float winding = MathF.Abs(sum);
                float value;

                if (rule == FillRule.NonZero)
                {
                    value = MathF.Min(winding, 1f);
                }
                else
                {
                    // the winding folded into 0..1..0 every two turns
                    float folded = winding % 2f;
                    value = folded > 1f ? 2f - folded : folded;
                }

                coverage[output + x] = (byte)(value * 255f + 0.5f);
            }
        }
    }
}