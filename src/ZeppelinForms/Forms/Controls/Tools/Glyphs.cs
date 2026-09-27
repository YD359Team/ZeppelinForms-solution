using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls.Tools;

/// <summary>Small pointers shared by several controls.</summary>
public static class Glyphs
{
    /// <summary>A chevron: right when collapsed, down when expanded.
    /// Drawn as a polyline rather than a font character — the glyph may be
    /// missing from the font, and the user would see a box instead of an arrow.</summary>
    public static void DrawChevron(
        Graphics g, Point center, float radius, bool expanded, Color color, float thickness = 1.8f)
    {
        float cx = center.X;
        float cy = center.Y;
        float r = radius;

        ReadOnlySpan<Point> arrow = expanded
            ? [new(cx - r, cy - r * 0.6f), new(cx, cy + r * 0.8f), new(cx + r, cy - r * 0.6f)]
            : [new(cx - r * 0.6f, cy - r), new(cx + r * 0.8f, cy), new(cx - r * 0.6f, cy + r)];

        g.DrawPolyline(arrow, color, thickness);
    }
}