using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>The edges of a <see cref="SafeArea"/> that keep out of the system's insets.</summary>
[Flags]
public enum SafeAreaEdges : byte
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    All = Left | Top | Right | Bottom,
}

/// <summary>
/// Keeps its content out of what the system covers — the status bar, a camera's notch,
/// the gesture bar: the form's <see cref="Form.SafeAreaInsets"/> is added to the padding
/// on the chosen edges. Its background, when it has one, still runs under them.
/// </summary>
/// <remarks>
/// For the content of a form that draws edge to edge (<see cref="Form.EdgeToEdge"/>), or
/// of a page in a browser on a phone. A <see cref="Scaffold"/> needs none: its app bar and
/// its navigation bar do the same for the top and the bottom.
/// </remarks>
public class SafeArea : DecoratedWrapControl
{
    private Form? _form;

    public SafeArea() { }

    public SafeArea(UIElement child) : base(child) { }

    /// <summary>Which edges keep out of the insets; all of them by default.</summary>
    public SafeAreaEdges Edges
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = SafeAreaEdges.All;

    /// <summary>The insets the content keeps out of: the form's, on the chosen edges.</summary>
    public Thickness Insets => Pick(_form?.SafeAreaInsets ?? Thickness.Zero, Edges);

    internal static Thickness Pick(Thickness insets, SafeAreaEdges edges) => new(
        edges.HasFlag(SafeAreaEdges.Left) ? insets.Left : 0f,
        edges.HasFlag(SafeAreaEdges.Top) ? insets.Top : 0f,
        edges.HasFlag(SafeAreaEdges.Right) ? insets.Right : 0f,
        edges.HasFlag(SafeAreaEdges.Bottom) ? insets.Bottom : 0f);

    protected override void OnAttached()
    {
        base.OnAttached();

        _form = FindOwner();

        if (_form is not null)
            _form.SafeAreaInsetsChanged += OnInsetsChanged;
    }

    protected override void OnDetached()
    {
        base.OnDetached();

        if (_form is not null)
            _form.SafeAreaInsetsChanged -= OnInsetsChanged;

        _form = null;
    }

    private void OnInsetsChanged(object? sender, EventArgs e) => Invalidate();

    protected override Size MeasureOverride(Size availableSize)
    {
        Thickness inset = Insets;
        Thickness padding = Padding;

        var content = new Size(
            Math.Max(0, availableSize.Width - padding.Horizontal - inset.Horizontal),
            Math.Max(0, availableSize.Height - padding.Vertical - inset.Vertical));

        Size child = Size.Empty;

        if (Child is not null)
        {
            Child.Measure(content);
            child = Child.DesiredSize;
        }

        return ResolveSize(
            new Size(
                child.Width + padding.Horizontal + inset.Horizontal,
                child.Height + padding.Vertical + inset.Vertical),
            availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Thickness inset = Insets;
        Thickness padding = Padding;

        Child?.Arrange(new Rectangle(
            new Point(padding.Left + inset.Left, padding.Top + inset.Top),
            new Size(
                Math.Max(0, finalSize.Width - padding.Horizontal - inset.Horizontal),
                Math.Max(0, finalSize.Height - padding.Vertical - inset.Vertical))));

        return finalSize;
    }
}
