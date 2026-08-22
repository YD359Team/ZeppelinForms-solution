namespace ZeppelinForms.Drawing.Primitives;

public readonly record struct Point
{
    public static readonly Point Empty;

    public readonly float X { get; }
    public readonly float Y { get; }

    public Point(float x, float y)
    {
        this.X = x;
        this.Y = y;
    }

    public static float DistanceBetween(Point pt1, Point pt2)
    {
        float dx = pt1.X - pt2.X;
        float dy = pt1.Y - pt2.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public static float DistanceBetweenM(Point pt1, Point pt2)
    {
        float dx = pt1.X - pt2.X;
        float dy = pt1.Y - pt2.Y;
        return MathF.Sqrt(dx * dx) + MathF.Sqrt(dy * dy);
    }
}
