using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Theming;

public sealed record ControlStyle
{
    public Color? Background { get; init; }
    public Color? BackgroundHover { get; init; }
    public Color? BackgroundPressed { get; init; }
    public Color? Border { get; init; }
    public Color? BorderFocus { get; init; }
    public Color? Text { get; init; }
    public Color? Accent { get; init; }
    public CornerRadius? CornerRadius { get; init; }
}