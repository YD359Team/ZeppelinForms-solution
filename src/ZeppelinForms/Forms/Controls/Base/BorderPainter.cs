using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>The border of a decorated element, drawn inside its box, as CSS and
/// WinUI draw it.</summary>
/// <remarks>
/// <para>
/// The stroke used to run along the box's edge, half of it outside. A parent cuts
/// its children at its own edge, so an element touching the top of a row lost the
/// outer half of its top line and kept the whole bottom one: the top looked thinner.
/// Inside the box nothing cuts it, and it ends where the background ends.
/// </para>
/// <para>
/// The box is aligned to device pixels first (<see cref="Graphics.AlignToPixels"/>),
/// the background with it, so the line falls on whole pixels on every side.
/// </para>
/// </remarks>
internal static class BorderPainter
{
    public static void Draw(Graphics g, Rectangle box, CornerRadius radius, Color color, float width)
    {
        if (width <= 0 || color.A == 0) return;

        g.DrawRoundRectangle(Inside(box, width), Inside(radius, width), color, width);
    }

    /// <summary>The line the stroke's middle runs along: half the width inside the box.</summary>
    public static Rectangle Inside(Rectangle box, float width)
    {
        float half = width / 2f;

        return new Rectangle(
            new Point(box.X + half, box.Y + half),
            new Size(Math.Max(0, box.Width - width), Math.Max(0, box.Height - width)));
    }

    /// <summary>The radii of that line: the outer curve keeps the radius asked for.</summary>
    public static CornerRadius Inside(CornerRadius radius, float width)
    {
        if (radius.IsZero) return radius;

        float half = width / 2f;

        return new CornerRadius(
            Math.Max(0, radius.TopLeft - half),
            Math.Max(0, radius.TopRight - half),
            Math.Max(0, radius.BottomRight - half),
            Math.Max(0, radius.BottomLeft - half));
    }
}