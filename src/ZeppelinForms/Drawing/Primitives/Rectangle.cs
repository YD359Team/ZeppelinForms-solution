namespace ZeppelinForms.Drawing.Primitives;

public record struct Rectangle
{
    public static readonly Rectangle Empty;

    public readonly float X { get; }
    public readonly float Y { get; }
    public readonly float Width { get; }
    public readonly float Height { get; }

    public Rectangle(float x, float y, float width, float height)
    {
        this.X = x;
        this.Y = y;
        this.Width = width;
        this.Height = height;
    }

    public readonly Point AsPosition() => new Point(this.X, this.Y);

    public readonly Size AsSize() => new Size(this.Width, this.Height);
}  
