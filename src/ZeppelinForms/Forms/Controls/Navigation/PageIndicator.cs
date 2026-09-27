using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Navigation;

/// <summary>
/// Page switcher dots. Binds to a PageControl and follows
/// its current page.
/// </summary>
public partial class PageIndicator : DecoratedControl
{
    private PageControl? _target;
    private int _hoveredIndex = -1;

    // the style and all the sizes define the indicator's desired size,
    // so each asks for a layout pass

    public PageIndicatorStyle Style
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = PageIndicatorStyle.Dots;

    public float DotSize
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = 9f;

    public float ActiveDotSize
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = 11f;

    public float Spacing
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = 8f;

    [Styled(Category = "Navigation")]
    public partial Color ActiveColor { get; set; }
    private static Color ActiveColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    [Styled(Category = "Navigation")]
    public partial Color InactiveColor { get; set; }
    private static Color InactiveColorDefault => new(255, 200, 200, 200);

    [Styled(Category = "Navigation")]
    public partial Color HoverColor { get; set; }
    private static Color HoverColorDefault => new(255, 150, 150, 150);

    public bool IsInteractive { get; set; } = true;

    public PageControl? Target
    {
        get => _target;
        set
        {
            if (ReferenceEquals(_target, value)) return;

            if (_target is not null)
                _target.Navigated -= OnTargetNavigated;

            _target = value;

            if (value is not null)
                value.Navigated += OnTargetNavigated;

            Invalidate();
        }
    }

    public PageIndicator()
    {
        SetControlDefault(PaddingProperty, new(4));
        Cursor = CursorKind.Hand;
    }

    private void OnTargetNavigated(object? sender, Page page) => InvalidateVisual();

    private List<Page> Pages
    {
        get
        {
            List<Page> pages = [];

            if (_target is null) return pages;

            foreach (UIElement child in _target.Children)
                if (child is Page page)
                    pages.Add(page);

            return pages;
        }
    }

    private float ItemWidth => Style == PageIndicatorStyle.Numbers
        ? TextMeasurer.Current.MeasureText("00", EffectiveFont).Width + 10f
        : Style == PageIndicatorStyle.Dashes ? DotSize * 2.4f : ActiveDotSize;

    protected override void DrawContent(Graphics g)
    {
        List<Page> pages = Pages;
        if (pages.Count == 0) return;

        Rectangle content = ContentBounds;
        float itemWidth = ItemWidth;

        float totalWidth = pages.Count * itemWidth + (pages.Count - 1) * Spacing;
        float x = content.X + (content.Width - totalWidth) / 2f;
        float centerY = content.Y + content.Height / 2f;

        for (int i = 0; i < pages.Count; i++)
        {
            bool active = ReferenceEquals(pages[i], _target!.CurrentPage);
            bool hovered = i == _hoveredIndex;

            Color color = active ? ActiveColor : (hovered ? HoverColor : InactiveColor);

            switch (Style)
            {
                case PageIndicatorStyle.Numbers:
                    {
                        var rect = new Rectangle(
                            new Point(x, centerY - content.Height / 2f),
                            new Size(itemWidth, content.Height));

                        if (active)
                            g.FillRoundRectangle(rect, new CornerRadius(content.Height / 2f), color);

                        g.DrawText((i + 1).ToString(), rect,
                            active ? Colors.White : color, EffectiveFont,
                            HorizontalContentAlignment.Center, VerticalContentAlignment.Center);

                        break;
                    }

                case PageIndicatorStyle.Dashes:
                    {
                        // the active dash is wider than the others — that way
                        // the position reads even with many pages
                        float width = active ? itemWidth : itemWidth * 0.55f;
                        float height = DotSize * 0.45f;

                        var rect = new Rectangle(
                            new Point(x + (itemWidth - width) / 2f, centerY - height / 2f),
                            new Size(width, height));

                        g.FillRoundRectangle(rect, new CornerRadius(height / 2f), color);
                        break;
                    }

                default:
                    {
                        float size = active ? ActiveDotSize : DotSize;

                        var rect = new Rectangle(
                            new Point(x + (itemWidth - size) / 2f, centerY - size / 2f),
                            new Size(size, size));

                        if (active)
                        {
                            g.FillEllipse(rect, color);
                        }
                        else
                        {
                            // inactive ones as outlines, so that the active one stands out with its fill
                            g.DrawEllipse(rect, color, 1.4f);
                        }

                        break;
                    }
            }

            x += itemWidth + Spacing;
        }
    }

    private int IndexFromPoint(Point location)
    {
        List<Page> pages = Pages;
        if (pages.Count == 0) return -1;

        Point abs = GetAbsolutePosition();
        Rectangle content = ContentBounds;

        float itemWidth = ItemWidth;
        float totalWidth = pages.Count * itemWidth + (pages.Count - 1) * Spacing;
        float startX = content.X + (content.Width - totalWidth) / 2f;

        float localX = location.X - abs.X;

        for (int i = 0; i < pages.Count; i++)
        {
            float left = startX + i * (itemWidth + Spacing);

            // the hit zone includes half of the gap on each side:
            // the dots are small, and aiming exactly at them is inconvenient
            if (localX >= left - Spacing / 2f && localX <= left + itemWidth + Spacing / 2f)
                return i;
        }

        return -1;
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (!IsInteractive) return;

        int index = IndexFromPoint(e.Location);
        if (index == _hoveredIndex) return;

        _hoveredIndex = index;
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        _hoveredIndex = -1;
        InvalidateVisual();
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        if (!IsInteractive || _target is null) return;

        int index = IndexFromPoint(e.Location);
        List<Page> pages = Pages;

        if (index < 0 || index >= pages.Count) return;

        e.Handled = true;

        // the transition direction depends on whether we go forward or back —
        // otherwise the slide would always go one way
        int currentIndex = pages.FindIndex(p => ReferenceEquals(p, _target.CurrentPage));

        PageTransition transition = _target.Transition;

        if (currentIndex >= 0 && index < currentIndex)
        {
            transition = transition switch
            {
                PageTransition.SlideLeft => PageTransition.SlideRight,
                PageTransition.SlideRight => PageTransition.SlideLeft,
                PageTransition.SlideUp => PageTransition.SlideDown,
                PageTransition.SlideDown => PageTransition.SlideUp,
                _ => transition,
            };
        }

        _target.Navigate(pages[index].Name, transition);
    }

    /// <summary>Subscribe again after coming back into the tree. OnDetached drops
    /// the subscription so that a discarded indicator doesn't live on through the
    /// PageControl, but the Target setter sees the same value and does nothing —
    /// so an indicator that was detached and attached again (a TabControl tab,
    /// a rebuilt panel) stopped following the pages for good.</summary>
    protected override void OnAttached()
    {
        if (_target is null) return;

        // -= first: an indicator attached for the first time is already subscribed
        // by the Target setter, and a second subscription would double every redraw
        _target.Navigated -= OnTargetNavigated;
        _target.Navigated += OnTargetNavigated;

        InvalidateVisual();
    }

    protected override void OnDetached()
    {
        if (_target is not null)
            _target.Navigated -= OnTargetNavigated;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        List<Page> pages = Pages;

        if (pages.Count == 0)
            return ResolveSize(new Size(Padding.Horizontal, Padding.Vertical), availableSize);

        float itemWidth = ItemWidth;
        float height = Style == PageIndicatorStyle.Numbers
            ? TextMeasurer.Current.MeasureText("0", EffectiveFont).Height + 8f
            : ActiveDotSize;

        return ResolveSize(
            new Size(
                pages.Count * itemWidth + (pages.Count - 1) * Spacing + Padding.Horizontal,
                height + Padding.Vertical),
            availableSize);
    }
}