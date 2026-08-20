namespace ZeppelinForms.Drawing.Primitives;

public readonly record struct Rectangle
{
    public static readonly Rectangle Empty;

    public readonly float X { get; }
    public readonly float Y { get; }
    public readonly float Width { get; }
    public readonly float Height { get; }

    public readonly Point Center => new(X + (Width / 2), Y + (Height / 2));

    public Rectangle(float x, float y, float width, float height)
    {
        this.X = x;
        this.Y = y;
        this.Width = width;
        this.Height = height;
    }

    public Rectangle(Point position, Size size)
    {
        this.X = position.X;
        this.Y = position.Y;
        this.Width = size.Width;
        this.Height = size.Height;
    }

    public readonly bool Contains(Point p)
    {
        return p.X >= X && p.X <= X + Width &&
               p.Y >= Y && p.Y <= Y + Height;
    }

    public readonly Point AsPosition() => new(this.X, this.Y);

    public readonly Size AsSize() => new(this.Width, this.Height);
}  
