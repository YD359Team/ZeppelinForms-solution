namespace ZeppelinForms.Drawing.Primitives;

public readonly record struct Rectangle
{
    public static readonly Rectangle Empty;

    public readonly float X { get; }
    public readonly float Y { get; }
    public readonly float Width { get; }
    public readonly float Height { get; }

    public readonly Point Center => new(X + (Width / 2), Y + (Height / 2));
    public readonly float Right => X + Width;
    public readonly float Bottom => Y + Height;

    public readonly Point Position => new(this.X, this.Y);

    public readonly Size Size => new(this.Width, this.Height);

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

    public bool IntersectsWith(Rectangle other) =>
    X < other.X + other.Width && other.X < X + Width &&
    Y < other.Y + other.Height && other.Y < Y + Height;

    public Rectangle Union(Rectangle other)
    {
        float left = Math.Min(X, other.X);
        float top = Math.Min(Y, other.Y);
        float right = Math.Max(X + Width, other.X + other.Width);
        float bottom = Math.Max(Y + Height, other.Y + other.Height);

        return new Rectangle(new Point(left, top), new Size(right - left, bottom - top));
    }

    public Rectangle Inflate(float amount) =>
        new(new Point(X - amount, Y - amount), new Size(Width + amount * 2, Height + amount * 2));
}  
