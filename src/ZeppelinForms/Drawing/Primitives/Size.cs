namespace ZeppelinForms.Drawing.Primitives;

public readonly record struct Size
{
    public static readonly Size Empty;
    public static readonly Size Auto = new(float.NaN, float.NaN);

    public readonly float Width { get; }
    public readonly float Height { get; }

    public bool IsWidthAuto => float.IsNaN(Width);
    public bool IsHeightAuto => float.IsNaN(Height);

    public Size(float width, float height)
    {
        this.Width = width;
        this.Height = height;
    }

    public static Size operator *(Size a, float multiplier) =>
        new(a.Width * multiplier, a.Height * multiplier);
}
