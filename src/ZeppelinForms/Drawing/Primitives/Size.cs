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
}
