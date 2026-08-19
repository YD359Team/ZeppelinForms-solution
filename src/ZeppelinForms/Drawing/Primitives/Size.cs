namespace ZeppelinForms.Drawing.Primitives;

public readonly record struct Size
{
    public static readonly Size Empty;

    public readonly float Width { get; }
    public readonly float Height { get; }

    public Size(float width, float height)
    {
        this.Width = width;
        this.Height = height;
    }

    // easy way to zoom
    public static Size operator *(Size a, float multiplier)
    {
        return new Size(a.Width * multiplier, a.Height * multiplier);
    }
}
