namespace ZeppelinForms.Drawing.Primitives;

public readonly record struct Thickness(float Left, float Top, float Right, float Bottom)
{
    public Thickness(float uniform) : this(uniform, uniform, uniform, uniform) { }
    public Thickness(float horizontal, float vertical) : this(horizontal, vertical, horizontal, vertical) { }

    public static readonly Thickness Zero = new(0);

    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;
}
