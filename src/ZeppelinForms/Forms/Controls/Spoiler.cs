using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Tools;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Control can collapse\expand child content
/// </summary>
public partial class Spoiler : DecoratedWrapControl
{
    private bool _headerHovered;

    public string? Header
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    }

    public float HeaderHeight
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the header is part of the spoiler's height
            Invalidate();
        }
    } = 26f;

    [Styled(Category = "Header")]
    public partial Color HeaderColor { get; set; }
    private static Color HeaderColorDefault => new(255, 245, 245, 245);

    [Styled(Category = "Header")]
    public partial Color HeaderHoverColor { get; set; }
    private static Color HeaderHoverColorDefault => new(255, 232, 232, 232);

    [Styled(Category = "Header")]
    public partial Color HeaderTextColor { get; set; }
    private static Color HeaderTextColorDefault => Colors.Black;

    public bool IsCollapsed
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnCollapsedStateChanged(value);
            Invalidate();
        }
    }

    public Spoiler()
    {
        // a collapsed spoiler must shrink to its header
        // rather than stretch over the whole allotted height
        SetControlDefault(VerticalAlignmentProperty, VerticalAlignment.Top);

        SetControlDefault(BorderColorProperty, new Color(255, 200, 200, 200));
        SetControlDefault(BorderWidthProperty, 1f);
    }

    // this(), not base(): the defaults above must apply to a spoiler created
    // with content too. With base() a collapsed spoiler from this constructor
    // stretched over the whole height and had no border
    public Spoiler(UIElement child) : this()
    {
        Child = child;
    }

    private Rectangle HeaderRect => new(Point.Empty, new Size(ActualSize.Width, HeaderHeight));

    // The child is hidden via IsVisible rather than by "skipping Arrange":
    // the renderer and panels respect this flag, while an unarranged element
    // would keep its old geometry and keep being drawn.
    private void SyncChildVisibility()
    {
        if (Child is not null)
            Child.IsVisible = !IsCollapsed;
    }

    protected override void DrawContent(Graphics g)
    {
        var header = HeaderRect;

        g.FillRectangle(header, _headerHovered ? HeaderHoverColor : HeaderColor);

        // the pointer triangle: right when collapsed, down when expanded
        Glyphs.DrawChevron(g, new Point(12f, HeaderHeight / 2f), 4f, !IsCollapsed, HeaderTextColor);

        if (!string.IsNullOrEmpty(Header))
        {
            var textRect = new Rectangle(
                new Point(24f, 0),
                new Size(Math.Max(0, ActualSize.Width - 28f), HeaderHeight));

            g.DrawText(Header, textRect, HeaderTextColor, EffectiveFont,
                HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
        }
    }

    protected override void OnMouseMove(MouseMoveEventArgs args)
    {
        Point abs = GetAbsolutePosition();
        bool inHeader = args.Location.Y - abs.Y <= HeaderHeight;

        if (inHeader != _headerHovered)
        {
            _headerHovered = inHeader;

            // hover changes only the header's color, not the geometry
            InvalidateVisual();
        }
    }

    protected override void OnMouseExit(MouseMoveEventArgs args)
    {
        if (!_headerHovered) return;

        _headerHovered = false;

        // without a redraw the header stayed highlighted after the mouse left
        InvalidateVisual();
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        Point abs = GetAbsolutePosition();

        // toggle only on a click in the header — clicks on the content
        // must go to the content itself
        if (e.Location.Y - abs.Y <= HeaderHeight)
        {
            IsCollapsed = !IsCollapsed;
            e.Handled = true;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        SyncChildVisibility();

        if (IsCollapsed)
            return ResolveSize(new Size(Padding.Horizontal, HeaderHeight + Padding.Vertical), availableSize);

        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical - HeaderHeight));

        Size childDesired = Size.Empty;
        if (Child is not null)
        {
            Child.Measure(inner);
            childDesired = Child.DesiredSize;
        }

        var content = new Size(
            childDesired.Width + Padding.Horizontal,
            childDesired.Height + Padding.Vertical + HeaderHeight);

        return ResolveSize(content, availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (IsCollapsed || Child is null)
            return finalSize;

        Child.Arrange(new Rectangle(
            new Point(Padding.Left, Padding.Top + HeaderHeight),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical - HeaderHeight))));

        return finalSize;
    }

    protected virtual void OnCollapsedStateChanged(bool isCollapsed)
    {
        SyncChildVisibility();
    }
}