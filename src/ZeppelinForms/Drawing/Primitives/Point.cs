namespace ZeppelinForms.Drawing.Primitives;

public record struct Point
{
    public static readonly Point Empty;

    public readonly float X { get; }
    public readonly float Y { get; }

    public Point(float x, float y)
    {
        this.X = x;
        this.Y = y;
    }
}
