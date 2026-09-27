using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Content that depends on the size class. Rebuilt only when the class
/// changes — that is, on a screen rotation or a real crossing of a boundary,
/// not on every movement of the window frame.
/// </summary>
public sealed class AdaptiveLayout : LayoutBuilder
{
    private Func<SizeClass, UIElement>? _content;

    public AdaptiveLayout() => RebuildKey = static size => Breakpoints.Classify(size);

    public Func<SizeClass, UIElement>? Content
    {
        get => _content;
        set
        {
            _content = value;

            Builder = value is null
                ? null
                : size => value(Breakpoints.Classify(size));

            Rebuild();
        }
    }
}