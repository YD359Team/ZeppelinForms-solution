using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ZeppelinForms.Animation;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Control with any count of children
/// </summary>
public abstract partial class PanelControl : UIElement, ITouchScrollTarget
{
    protected const float ScrollBarThickness = 10f;
    private const float MinThumbLength = 24f;

    private Size _contentSize;
    private bool _draggingVertical;
    private bool _draggingHorizontal;
    private float _dragOffset;

    public ObservableCollection<UIElement> Children { get; } = [];

    /// <summary>The children this panel has attached to itself.</summary>
    /// <remarks>
    /// ObservableCollection reports Clear() as Reset without OldItems, so the
    /// collection itself cannot say who left. Without this set the cleared
    /// children kept Parent pointing here and stayed attached to the form —
    /// focus, animations and flyouts still saw them.
    /// </remarks>
    private readonly HashSet<UIElement> _attached = new(ReferenceEqualityComparer.Instance);

    /// <summary>The travel rule for all children at once. A child with no rule
    /// of its own takes it: a list row has no way to know that the list
    /// can rearrange smoothly.</summary>
    public LayoutTransition? ChildrenLayoutTransition { get; set; }

    /// <summary>How children added to an already shown panel appear.
    /// A child with no rule of its own takes it.</summary>
    public VisibilityTransition? ChildrenEnterTransition { get; set; }

    /// <summary>How removed children disappear. A child with no rule
    /// of its own takes it.</summary>
    public VisibilityTransition? ChildrenExitTransition { get; set; }

    private List<ExitingChild>? _exiting;

    /// <summary>Exiting children: already removed from Children — layout,
    /// hit testing and the neighbours don't know about them — but still drawn
    /// on top until their disappearance finishes playing.</summary>
    internal IReadOnlyList<ExitingChild>? Exiting => _exiting;

    internal void RemoveExiting(ExitingChild exiting)
    {
        if (_exiting is null) return;

        _exiting.Remove(exiting);

        if (_exiting.Count == 0) _exiting = null;

        InvalidateVisual();
    }

    /// <summary>Start the disappearance of a child being removed, if it is entitled
    /// to one. Called before detaching from the form: after that the element has
    /// neither an owner nor frames.</summary>
    private void BeginExit(UIElement item, Form? owner)
    {
        // the mark is consumed in any case: it is one-shot
        if (item.ConsumeSkipExit()) return;

        if (owner is null) return;
        if (!item.CanAnimateExit) return;
        if (item.ExitTransitionIn(this) is not { } rule) return;

        // a panel that isn't visible doesn't show disappearances either
        if (!IsEffectivelyVisible) return;

        var exiting = new ExitingChild(this, item, rule);

        _exiting ??= [];
        _exiting.Add(exiting);

        owner.AddAnimation(exiting);
    }

    /// <summary>The element was returned while it was still disappearing:
    /// it must not be drawn twice — alive and as a ghost.</summary>
    private void CancelExit(UIElement item)
    {
        if (_exiting is null) return;

        for (int i = _exiting.Count - 1; i >= 0; i--)
            if (ReferenceEquals(_exiting[i].Element, item))
                _exiting[i].Cancel(applyFinalValue: false);
    }

    protected internal override Rectangle ClipBounds => Viewport;

    // overflow decides whether the content may grow along an axis,
    // that is, it directly affects measuring

    public Overflow OverflowX
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            UpdatePanRecognizer();
            Invalidate();
        }
    } = Overflow.Visible;

    public Overflow OverflowY
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            UpdatePanRecognizer();
            Invalidate();
        }
    } = Overflow.Visible;

    public float ScrollX { get; private set; }
    public float ScrollY { get; private set; }

    public float WheelStep { get; set; } = 48f;

    [Styled(Category = "Scrolling")]
    public partial Color ScrollTrackColor { get; set; }
    private static Color ScrollTrackColorDefault => new(40, 0, 0, 0);

    [Styled(Category = "Scrolling")]
    public partial Color ScrollThumbColor { get; set; }
    private static Color ScrollThumbColorDefault => new(120, 0, 0, 0);

    protected bool ScrollsHorizontally => OverflowX is Overflow.Scroll or Overflow.Auto;
    protected bool ScrollsVertically => OverflowY is Overflow.Scroll or Overflow.Auto;

    // _contentSize comes from MeasureContentOverride including padding,
    // so the full size is subtracted rather than ContentBounds — otherwise
    // the padding would be counted twice and you could scroll past the end of the content
    private float MaxScrollX => Math.Max(0, _contentSize.Width - ActualSize.Width + ReservedWidth);
    private float MaxScrollY => Math.Max(0, _contentSize.Height - ActualSize.Height + ReservedHeight);

    public ScrollBarMode ScrollBarMode
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // in Inline mode the bar takes space away from the content
            Invalidate();
        }
    } = ScrollBarMode.Overlay;

    // visibility is computed once per layout and only read after that:
    // in Inline mode the bars take space from each other, and recomputing
    // on every access would give different answers in different places of the frame
    private bool _verticalBar;
    private bool _horizontalBar;

    /// <summary>The visible area as of the last arrange.</summary>
    /// <remarks>
    /// Measuring along a scrollable axis gets infinity, so virtualization
    /// has no other way to learn how many rows are visible. Viewport doesn't
    /// fit this: it looks at ActualSize, which inside ArrangeOverride is still
    /// the previous one. Here the size is from the current arrange — exact
    /// in ArrangeContentOverride, and from the previous pass
    /// in MeasureContentOverride.
    /// </remarks>
    protected Size ArrangedViewport { get; private set; }

    protected bool ShowVerticalBar => _verticalBar;
    protected bool ShowHorizontalBar => _horizontalBar;

    private float ReservedWidth => ScrollBarMode == ScrollBarMode.Inline && _verticalBar ? ScrollBarThickness : 0f;
    private float ReservedHeight => ScrollBarMode == ScrollBarMode.Inline && _horizontalBar ? ScrollBarThickness : 0f;

    /// <summary>The visible content area: ContentBounds minus the space
    /// for the bars. In Overlay mode it equals ContentBounds.</summary>
    protected Rectangle Viewport
    {
        get
        {
            Rectangle c = ContentBounds;

            return new Rectangle(c.Position, new Size(
                Math.Max(0, c.Width - ReservedWidth),
                Math.Max(0, c.Height - ReservedHeight)));
        }
    }

    public PanelControl()
    {
        Children.CollectionChanged += Children_CollectionChanged;
    }

    /// <summary>While greater than zero, changes to the set of children don't
    /// request a layout pass. Needed by those who change Children right inside
    /// measuring: the containers are measured there too, and they don't need
    /// a second pass. Attaching to the tree still happens as usual — only
    /// the request for a layout pass is suppressed.</summary>
    private protected int SuppressChildrenInvalidate;

    private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Form? owner = FindOwner();

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ResetChildren(owner);
        }
        else
        {
            if (e.OldItems is not null)
                foreach (UIElement item in e.OldItems)
                {
                    // only a genuine removal disappears: on a move the element
                    // leaves and immediately comes back, and on a replace another
                    // one takes its place — there the departure is not visible to the eye
                    if (e.Action == NotifyCollectionChangedAction.Remove)
                        BeginExit(item, owner);

                    owner?.DetachTree(item);
                    item.Parent = null;
                    _attached.Remove(item);
                }

            if (e.NewItems is not null)
                foreach (UIElement item in e.NewItems)
                {
                    CancelExit(item);

                    item.Parent = this;
                    owner?.AttachTree(item);
                    _attached.Add(item);
                }
        }

        if (SuppressChildrenInvalidate == 0)
            Invalidate();
    }

    /// <summary>Bring attachment in line with Children after a Reset: detach
    /// those who left, attach those who are new.</summary>
    /// <remarks>
    /// A bulk clear gets no exit animation: Reset does not say what exactly
    /// left, and a wave of ghosts over an emptied panel is not what Clear means.
    /// </remarks>
    private void ResetChildren(Form? owner)
    {
        var current = new HashSet<UIElement>(Children, ReferenceEqualityComparer.Instance);

        foreach (UIElement item in _attached)
        {
            if (current.Contains(item)) continue;

            owner?.DetachTree(item);
            item.Parent = null;
        }

        foreach (UIElement item in Children)
        {
            if (_attached.Contains(item)) continue;

            CancelExit(item);

            item.Parent = this;
            owner?.AttachTree(item);
        }

        _attached.Clear();
        _attached.UnionWith(Children);
    }

    /// <summary>Whether bars are needed for this visible area. In Inline mode
    /// one bar may cause the second to appear, so after the first one we check
    /// again — one repeat is enough, after that the size no longer changes.</summary>
    private (bool Vertical, bool Horizontal) ResolveBars(Size available)
    {
        bool NeedsVertical(float height) =>
            OverflowY == Overflow.Scroll ||
            (OverflowY == Overflow.Auto && _contentSize.Height - height > 0.5f);

        bool NeedsHorizontal(float width) =>
            OverflowX == Overflow.Scroll ||
            (OverflowX == Overflow.Auto && _contentSize.Width - width > 0.5f);

        bool vertical = NeedsVertical(available.Height);
        bool horizontal = NeedsHorizontal(available.Width);

        if (ScrollBarMode != ScrollBarMode.Inline)
            return (vertical, horizontal);

        if (vertical) horizontal = NeedsHorizontal(available.Width - ScrollBarThickness);
        if (horizontal) vertical = NeedsVertical(available.Height - ScrollBarThickness);

        return (vertical, horizontal);
    }

    // ===== scrolling on top of the regular layout =====

    protected sealed override Size MeasureOverride(Size availableSize)
    {
        // along a scrollable axis the content may grow as much as it needs
        var probe = new Size(
            ScrollsHorizontally ? float.PositiveInfinity : availableSize.Width,
            ScrollsVertically ? float.PositiveInfinity : availableSize.Height);

        _contentSize = MeasureContentOverride(probe);

        if (ScrollBarMode == ScrollBarMode.Inline)
        {
            var (vertical, horizontal) = ResolveBars(availableSize);

            if (vertical || horizontal)
            {
                _contentSize = MeasureContentOverride(new Size(
                    ScrollsHorizontally ? probe.Width : probe.Width - (vertical ? ScrollBarThickness : 0),
                    ScrollsVertically ? probe.Height : probe.Height - (horizontal ? ScrollBarThickness : 0)));
            }
        }

        // the panel itself doesn't go beyond what it was given — the excess goes into scrolling
        return new Size(
            ScrollsHorizontally ? Math.Min(_contentSize.Width, availableSize.Width) : _contentSize.Width,
            ScrollsVertically ? Math.Min(_contentSize.Height, availableSize.Height) : _contentSize.Height);
    }

    protected sealed override Size ArrangeOverride(Size finalSize)
    {
        // ContentBounds here still looks at the previous ActualSize: it is
        // assigned only after returning from here. So both bar visibility
        // and the scroll limit are computed from finalSize
        (_verticalBar, _horizontalBar) = ResolveBars(finalSize);

        float reservedW = ScrollBarMode == ScrollBarMode.Inline && _verticalBar ? ScrollBarThickness : 0f;
        float reservedH = ScrollBarMode == ScrollBarMode.Inline && _horizontalBar ? ScrollBarThickness : 0f;

        var viewport = new Size(
            Math.Max(0, finalSize.Width - reservedW),
            Math.Max(0, finalSize.Height - reservedH));

        // the content is arranged at its full size, even if it is larger than the panel
        var contentArea = new Size(
            ScrollsHorizontally ? Math.Max(viewport.Width, _contentSize.Width) : viewport.Width,
            ScrollsVertically ? Math.Max(viewport.Height, _contentSize.Height) : viewport.Height);

        float maxX = Math.Max(0, _contentSize.Width - viewport.Width);
        float maxY = Math.Max(0, _contentSize.Height - viewport.Height);

        // clamp before arranging the content, not after: virtualization computes
        // the visible range from ScrollY right in ArrangeContentOverride, and after
        // a list collapsed it would get a scroll position past its end
        ScrollX = Math.Clamp(ScrollX, 0, maxX);
        ScrollY = Math.Clamp(ScrollY, 0, maxY);

        ArrangedViewport = viewport;

        ArrangeContentOverride(contentArea);

        if (IsRightToLeft)
        {
            // mirror the children around the panel's vertical axis
            foreach (UIElement child in Children)
                child.Position = new Point(
                    contentArea.Width - child.Position.X - child.ActualSize.Width,
                    child.Position.Y);
        }

        float shiftX = ScrollX + _overscroll.X;
        float shiftY = ScrollY + _overscroll.Y;

        if (shiftX != 0 || shiftY != 0)
        {
            // shift the already arranged children — that way concrete panels
            // need to know nothing about scrolling. Overscroll when scrolling
            // with a finger is the same shift, only beyond the allowed range
            foreach (UIElement child in Children)
                child.Position = new Point(child.Position.X - shiftX, child.Position.Y - shiftY);
        }

        return finalSize;
    }

    /// <summary>Measuring the content, as in a regular panel.</summary>
    protected abstract Size MeasureContentOverride(Size availableSize);

    /// <summary>Arranging the content. The size may exceed the panel's size when scrolling.</summary>
    protected abstract void ArrangeContentOverride(Size contentSize);

    public void ScrollTo(float x, float y)
    {
        // an explicit scroll from code or the wheel takes priority over fling inertia
        _touch?.Stop();

        ScrollX = Math.Clamp(x, 0, MaxScrollX);
        ScrollY = Math.Clamp(y, 0, MaxScrollY);
        Invalidate();
    }

    // ===== scrollbars =====

    private Rectangle VerticalBarRect
    {
        get
        {
            var c = ContentBounds;
            return new Rectangle(
                new Point(c.X + c.Width - ScrollBarThickness, c.Y),
                new Size(ScrollBarThickness, c.Height - (ShowHorizontalBar ? ScrollBarThickness : 0)));
        }
    }

    private Rectangle HorizontalBarRect
    {
        get
        {
            var c = ContentBounds;
            return new Rectangle(
                new Point(c.X, c.Y + c.Height - ScrollBarThickness),
                new Size(c.Width - (ShowVerticalBar ? ScrollBarThickness : 0), ScrollBarThickness));
        }
    }

    private (float Position, float Length) VerticalThumb
    {
        get
        {
            Rectangle bar = VerticalBarRect;
            float ratio = _contentSize.Height <= 0 ? 1 : Viewport.Height / _contentSize.Height;
            float length = Math.Max(MinThumbLength, bar.Height * Math.Min(1, ratio));
            float position = MaxScrollY <= 0 ? 0 : (bar.Height - length) * (ScrollY / MaxScrollY);
            return (position, length);
        }
    }

    private (float Position, float Length) HorizontalThumb
    {
        get
        {
            Rectangle bar = HorizontalBarRect;
            float ratio = _contentSize.Width <= 0 ? 1 : Viewport.Width / _contentSize.Width;
            float length = Math.Max(MinThumbLength, bar.Width * Math.Min(1, ratio));
            float position = MaxScrollX <= 0 ? 0 : (bar.Width - length) * (ScrollX / MaxScrollX);
            return (position, length);
        }
    }

    protected internal override void DrawOverlay(Graphics g)
    {
        if (ShowVerticalBar)
        {
            Rectangle bar = VerticalBarRect;
            var (pos, len) = VerticalThumb;

            g.FillRoundRectangle(bar, new CornerRadius(ScrollBarThickness / 2f), ScrollTrackColor);
            g.FillRoundRectangle(
                new Rectangle(new Point(bar.X + 2, bar.Y + pos), new Size(ScrollBarThickness - 4, len)),
                new CornerRadius((ScrollBarThickness - 4) / 2f), ScrollThumbColor);
        }

        if (ShowHorizontalBar)
        {
            Rectangle bar = HorizontalBarRect;
            var (pos, len) = HorizontalThumb;

            g.FillRoundRectangle(bar, new CornerRadius(ScrollBarThickness / 2f), ScrollTrackColor);
            g.FillRoundRectangle(
                new Rectangle(new Point(bar.X + pos, bar.Y + 2), new Size(len, ScrollBarThickness - 4)),
                new CornerRadius((ScrollBarThickness - 4) / 2f), ScrollThumbColor);
        }
    }

    protected internal override bool HitTestSelfFirst(Point localPoint)
    {
        // clicks on a bar belong to the panel, not to whatever is under it
        if (ShowVerticalBar && Contains(VerticalBarRect, localPoint)) return true;
        if (ShowHorizontalBar && Contains(HorizontalBarRect, localPoint)) return true;

        return false;
    }

    private static bool Contains(Rectangle r, Point p) =>
        p.X >= r.X && p.X <= r.X + r.Width && p.Y >= r.Y && p.Y <= r.Y + r.Height;

    protected override void OnMouseDown(MouseButtonEventArgs args)
    {
        Point abs = GetAbsolutePosition();
        var local = new Point(args.Location.X - abs.X, args.Location.Y - abs.Y);

        if (ShowVerticalBar && Contains(VerticalBarRect, local))
        {
            var (pos, len) = VerticalThumb;
            float offsetInBar = local.Y - VerticalBarRect.Y;

            if (offsetInBar >= pos && offsetInBar <= pos + len)
            {
                _draggingVertical = true;
                _dragOffset = offsetInBar - pos;

                // without capture the drag breaks off as soon as the cursor
                // leaves the window: the moves go to another window
                CaptureMouse();
            }
            else
            {
                ScrollTo(ScrollX, ScrollY + (offsetInBar < pos ? -Viewport.Height : Viewport.Height));
            }

            return;
        }

        if (ShowHorizontalBar && Contains(HorizontalBarRect, local))
        {
            var (pos, len) = HorizontalThumb;
            float offsetInBar = local.X - HorizontalBarRect.X;

            if (offsetInBar >= pos && offsetInBar <= pos + len)
            {
                _draggingHorizontal = true;
                _dragOffset = offsetInBar - pos;
                CaptureMouse();
            }
            else
            {
                ScrollTo(ScrollX + (offsetInBar < pos ? -Viewport.Width : Viewport.Width), ScrollY);
            }
        }
    }

    protected override void OnMouseMove(MouseMoveEventArgs args)
    {
        if (!_draggingVertical && !_draggingHorizontal) return;

        Point abs = GetAbsolutePosition();

        if (_draggingVertical)
        {
            Rectangle bar = VerticalBarRect;
            var (_, len) = VerticalThumb;
            float free = bar.Height - len;

            if (free > 0)
            {
                float t = (args.Location.Y - abs.Y - bar.Y - _dragOffset) / free;
                ScrollTo(ScrollX, MaxScrollY * Math.Clamp(t, 0, 1));
            }
        }
        else
        {
            Rectangle bar = HorizontalBarRect;
            var (_, len) = HorizontalThumb;
            float free = bar.Width - len;

            if (free > 0)
            {
                float t = (args.Location.X - abs.X - bar.X - _dragOffset) / free;
                ScrollTo(MaxScrollX * Math.Clamp(t, 0, 1), ScrollY);
            }
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs location)
    {
        EndThumbDrag();
    }

    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        // there will be no release after a cancel: without a reset the next
        // mouse move with no button pressed would keep dragging the thumb
        EndThumbDrag();
    }

    private void EndThumbDrag()
    {
        if (!_draggingVertical && !_draggingHorizontal) return;

        _draggingVertical = _draggingHorizontal = false;
        ReleaseMouseCapture();
    }

    // ===== touch scrolling =====

    private TouchScroller? _touch;

    /// <summary>Overscroll: content pulled by a finger further than allowed.
    /// ScrollX and ScrollY don't include it — they are always within range —
    /// so layout, the bars and code see the honest scroll position.</summary>
    private Point _overscroll;

    /// <summary>Whether content scrolls with a finger or a pen. Never with the mouse:
    /// on the desktop dragging with the mouse means selecting text
    /// and drag-and-drop, and scrolling belongs to the wheel.</summary>
    public bool PanToScroll { get; set; } = true;

    /// <summary>The recognizer lives only on a scrollable panel. Otherwise every
    /// StackPanel in the tree would take part in the fight for every press,
    /// and an arena would be created for everything.</summary>
    private void UpdatePanRecognizer()
    {
        bool scrolls = ScrollsHorizontally || ScrollsVertically;

        if (scrolls && _touch is null)
        {
            _touch = TouchScroller.Attach(this);
        }
        else if (!scrolls && _touch is not null)
        {
            _touch.Detach();
            _touch = null;
        }
    }

    UIElement ITouchScrollTarget.Element => this;

    bool ITouchScrollTarget.CanPanHorizontally => PanToScroll && ScrollsHorizontally && MaxScrollX > 0;

    bool ITouchScrollTarget.CanPanVertically => PanToScroll && ScrollsVertically && MaxScrollY > 0;

    Size ITouchScrollTarget.PanViewport => Viewport.Size;

    Point ITouchScrollTarget.PanScroll => new(ScrollX, ScrollY);

    Point ITouchScrollTarget.PanMaxScroll => new(MaxScrollX, MaxScrollY);

    void ITouchScrollTarget.ApplyPanScroll(Point scroll, Point overscroll)
    {
        ScrollX = scroll.X;
        ScrollY = scroll.Y;
        _overscroll = overscroll;

        Invalidate();
    }

    protected override void OnPointerDown(PointerEventArgs e)
    {
        // a touch in the middle of a fling stops the inertia — as on any phone.
        // The bounce from the edge still plays out, though: pulled content
        // must not freeze where the finger caught it
        if (e.Kind != PointerKind.Mouse) _touch?.StopFling();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (e.HorizontalDelta != 0 && ScrollsHorizontally)
        {
            ScrollTo(ScrollX - e.HorizontalDelta / 120f * WheelStep, ScrollY);
            e.Handled = true;

            return;
        }

        if (!ScrollsVertically || MaxScrollY <= 0) return;

        float before = ScrollY;
        ScrollTo(ScrollX, ScrollY - e.Delta / 120f * WheelStep);

        if (Math.Abs(before - ScrollY) > 0.01f)
            e.Handled = true;
    }
}

public enum ScrollBarMode
{
    /// <summary>The bar lies on top of the content: it takes no space,
    /// but covers what is under it.</summary>
    Overlay,

    /// <summary>The space for the bar is subtracted from the content area:
    /// the content is narrower, but nothing is covered.</summary>
    Inline,
}