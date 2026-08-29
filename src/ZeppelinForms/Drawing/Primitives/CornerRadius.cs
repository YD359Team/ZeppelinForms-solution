namespace ZeppelinForms.Drawing.Primitives;

public readonly record struct CornerRadius(float TopLeft, float TopRight, float BottomRight, float BottomLeft)
{
    public CornerRadius(float uniform) : this(uniform, uniform, uniform, uniform) { }

    public static readonly CornerRadius Zero = new(0);

    public bool IsZero => TopLeft == 0 && TopRight == 0 && BottomRight == 0 && BottomLeft == 0;
    public float Max => Math.Max(Math.Max(TopLeft, TopRight), Math.Max(BottomRight, BottomLeft));
}