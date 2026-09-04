using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Animation;

public static class Interpolators
{
    public static float Float(float a, float b, float t) => a + (b - a) * t;

    public static Color Color(Color a, Color b, float t) => new(
        (byte)Float(a.A, b.A, t),
        (byte)Float(a.R, b.R, t),
        (byte)Float(a.G, b.G, t),
        (byte)Float(a.B, b.B, t));

    public static Point Point(Point a, Point b, float t) =>
        new(Float(a.X, b.X, t), Float(a.Y, b.Y, t));

    public static Size Size(Size a, Size b, float t) =>
        new(Float(a.Width, b.Width, t), Float(a.Height, b.Height, t));
}
