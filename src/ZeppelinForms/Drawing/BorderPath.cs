using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing;

/// <summary>Walking a rounded rectangle's outline by length: where a comet of light along
/// a border is.</summary>
internal readonly struct BorderPath
{
    private readonly Rectangle _rect;
    private readonly float _tl, _tr, _br, _bl;
    private readonly float _top, _right, _bottom, _left;

    public BorderPath(Rectangle rect, CornerRadius radius)
    {
        _rect = rect;

        // each corner no larger than the shorter side allows
        float limit = MathF.Max(0f, MathF.Min(rect.Width, rect.Height) / 2f);

        _tl = Math.Clamp(radius.TopLeft, 0f, limit);
        _tr = Math.Clamp(radius.TopRight, 0f, limit);
        _br = Math.Clamp(radius.BottomRight, 0f, limit);
        _bl = Math.Clamp(radius.BottomLeft, 0f, limit);

        _top = MathF.Max(0f, rect.Width - _tl - _tr);
        _right = MathF.Max(0f, rect.Height - _tr - _br);
        _bottom = MathF.Max(0f, rect.Width - _br - _bl);
        _left = MathF.Max(0f, rect.Height - _bl - _tl);

        Length = _top + _right + _bottom + _left + MathF.PI / 2f * (_tl + _tr + _br + _bl);
    }

    /// <summary>The outline's whole length.</summary>
    public float Length { get; }

    /// <summary>The point at a distance along the outline, clockwise from where the top
    /// edge starts after the top-left corner; any distance, wrapped round the length.</summary>
    public Point At(float distance)
    {
        if (Length <= 0f) return new Point(_rect.X, _rect.Y);

        float s = distance % Length;
        if (s < 0f) s += Length;

        float x = _rect.X, y = _rect.Y, w = _rect.Width, h = _rect.Height;

        if (s <= _top) return new Point(x + _tl + s, y);
        s -= _top;

        if (Arc(ref s, _tr, x + w - _tr, y + _tr, -90f, out Point p)) return p;

        if (s <= _right) return new Point(x + w, y + _tr + s);
        s -= _right;

        if (Arc(ref s, _br, x + w - _br, y + h - _br, 0f, out p)) return p;

        if (s <= _bottom) return new Point(x + w - _br - s, y + h);
        s -= _bottom;

        if (Arc(ref s, _bl, x + _bl, y + h - _bl, 90f, out p)) return p;

        if (s <= _left) return new Point(x, y + h - _bl - s);
        s -= _left;

        Arc(ref s, _tl, x + _tl, y + _tl, 180f, out p);
        return p;
    }

    /// <summary>A quarter turn of a corner, clockwise from <paramref name="startDegrees"/>.</summary>
    private static bool Arc(ref float s, float radius, float cx, float cy, float startDegrees, out Point point)
    {
        float length = MathF.PI / 2f * radius;

        if (s > length || radius <= 0f)
        {
            s -= length;
            point = default;
            return false;
        }

        float angle = (startDegrees + s / radius * 180f / MathF.PI) * MathF.PI / 180f;
        point = new Point(cx + radius * MathF.Cos(angle), cy + radius * MathF.Sin(angle));
        return true;
    }
}
