using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls.Tools;

/// <summary>Мелкие указатели, общие для нескольких контролов.</summary>
public static class Glyphs
{
    /// <summary>Уголок: вправо когда свёрнуто, вниз когда раскрыто.
    /// Рисуется ломаной, а не символом шрифта — глиф в шрифте может
    /// отсутствовать, и вместо стрелки пользователь увидит квадрат.</summary>
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