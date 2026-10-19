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

    /// <summary>A size with the width given and the height left to the layout:
    /// <c>Size = Size.FromWidth(240)</c> instead of <c>new Size(240, float.NaN)</c>.</summary>
    public static Size FromWidth(float width) => new(width, float.NaN);

    /// <summary>A size with the height given and the width left to the layout:
    /// a text box docked to the top keeps stretching across.</summary>
    public static Size FromHeight(float height) => new(float.NaN, height);

    public static Size operator *(Size a, float multiplier) =>
        new(a.Width * multiplier, a.Height * multiplier);
}