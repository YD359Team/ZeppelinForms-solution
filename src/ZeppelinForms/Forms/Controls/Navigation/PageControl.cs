using ZeppelinForms.Animation;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Gestures;

namespace ZeppelinForms.Forms.Controls.Navigation;

/// <summary>
/// A container of views with a navigation history. Hidden pages stay
/// in the tree but are neither drawn nor receive events.
/// </summary>
public class PageControl : DecoratedPanel
{
    private readonly List<string> _history = [];
    private Page? _current;
    private Page? _outgoing;

    private float _progress = 1f;

    /// <summary>Create an indicator bound to this container.</summary>
    public PageIndicator CreateIndicator(PageIndicatorStyle style = PageIndicatorStyle.Dots) =>
        new() { Target = this, Style = style };

    public PageTransition Transition { get; set; } = PageTransition.SlideLeft;
    public int TransitionDurationMs { get; set; } = 220;

    /// <summary>Prepare the content of pages not yet shown while idle.</summary>
    /// <remarks>
    /// A page's factory runs on first show — right in the press handler. For a page
    /// of three dozen controls with pictures and charts that is a noticeable pause
    /// between the click and the start of the transition, and it is visible only
    /// the first time: after that the content is already built. So the other pages
    /// are built in advance while the user reads the current one, and one at a
    /// time — so as not to gather all the pauses into one.
    /// </remarks>
    public bool PreloadPages { get; set; } = true;

    /// <summary>The pause before the next portion of preparation.</summary>
    public int PreloadDelayMs { get; set; } = 150;

    private IDisposable? _preloadWake;

    public Page? CurrentPage => _current;

    public bool CanGoBack => _history.Count > 1;

    /// <summary>Give focus to the first text field of a page when it is shown,
    /// as the form does on first show.</summary>
    public bool FocusOnNavigate { get; set; } = true;

    public event EventHandler<Page>? Navigated;

    private SwipeGestureRecognizer? _swipe;

    /// <summary>How to react to a swipe over the content. Works with the mouse too:
    /// the recognizer tells fingers from the mouse only by thresholds.</summary>
    public PageSwipeMode SwipeMode
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            UpdateSwipeRecognizer();
        }
    } = PageSwipeMode.Back;

    public PageControl() => UpdateSwipeRecognizer();

    protected override void OnAttached()
    {
        base.OnAttached();

        // before attaching there is no clock, which means nowhere to defer the preparation to
        SchedulePreload(PreloadDelayMs);
    }

    protected override void OnDetached()
    {
        base.OnDetached();

        _preloadWake?.Dispose();
        _preloadWake = null;
    }

    private void SchedulePreload(int delayMs)
    {
        if (!PreloadPages) return;
        if (FindOwner() is not { } owner) return;

        _preloadWake?.Dispose();
        _preloadWake = owner.Schedule(delayMs, PreloadNext);
    }

    /// <summary>Build one unprepared page and sign up for the next portion.
    /// Exactly one at a time: building a page means creating dozens of controls
    /// with loading of their resources, and doing it in a batch gives the same
    /// noticeable pause, just somewhere else.</summary>
    private void PreloadNext()
    {
        _preloadWake = null;

        foreach (UIElement child in Children)
        {
            if (child is not Page { IsBuilt: false } page) continue;

            try
            {
                page.EnsureBuilt();
            }
            catch (Exception exception)
            {
                // preparation is work done ahead of time, and it must not crash
                // the application: a page the user hasn't opened yet must not be
                // lost either. The page stays unbuilt, so on navigating to it the
                // error will occur in the same place as without preparation
                System.Diagnostics.Debug.WriteLine(
                    $"ZF: page \"{page.Title ?? "untitled"}\" " +
                    $"could not be prepared in advance. {exception}");
            }

            SchedulePreload(PreloadDelayMs);

            return;
        }
    }

    private void UpdateSwipeRecognizer()
    {
        if (SwipeMode == PageSwipeMode.None)
        {
            if (_swipe is null) return;

            _swipe.Swiped -= OnSwiped;
            GestureRecognizers.Remove(_swipe);
            _swipe = null;

            return;
        }

        if (_swipe is null)
        {
            _swipe = this.AddGesture(new SwipeGestureRecognizer());
            _swipe.Swiped += OnSwiped;
        }

        _swipe.AllowedDirections = SwipeMode == PageSwipeMode.Back
            ? [SwipeDirection.Right]
            : [SwipeDirection.Left, SwipeDirection.Right];
    }

    private void OnSwiped(object? sender, SwipeGestureEventArgs e)
    {
        // a transition is already running: a second one on top of it
        // would leave _outgoing drawn halfway
        if (_progress < 1f) return;

        if (SwipeMode == PageSwipeMode.Back)
        {
            GoBack();
            return;
        }

        // left — forward: the content moves in the same direction as the finger
        int step = e.Direction == SwipeDirection.Left ? 1 : -1;

        if (PageAtOffset(step) is not Page target) return;

        Navigate(target.Name, step > 0 ? PageTransition.SlideLeft : PageTransition.SlideRight);
    }

    private Page? PageAtOffset(int offset)
    {
        List<Page> pages = [];

        foreach (UIElement child in Children)
            if (child is Page page)
                pages.Add(page);

        int index = _current is null ? -1 : pages.IndexOf(_current);
        if (index < 0) return null;

        int next = index + offset;

        // no wrap-around: jumping from the last page to the first
        // reads as a glitch, not as a transition
        return next >= 0 && next < pages.Count ? pages[next] : null;
    }

    /// <summary>Add a page. The first one added becomes the current one.</summary>
    public Page AddPage(string name, Func<UIElement> factory, string? title = null)
    {
        var page = new Page
        {
            Name = name,
            Title = title ?? name,
            ContentFactory = factory,
            IsVisible = false,
        };

        Children.Add(page);

        if (_current is null)
            Navigate(name, PageTransition.None);

        return page;
    }

    public void Navigate(string name) => Navigate(name, Transition);

    public void Navigate(string name, PageTransition transition)
    {
        Page? target = FindPage(name);
        if (target is null || ReferenceEquals(target, _current)) return;

        _history.Add(name);
        Switch(target, transition);
    }

    public void GoBack()
    {
        if (!CanGoBack) return;

        _history.RemoveAt(_history.Count - 1);

        Page? target = FindPage(_history[^1]);
        if (target is null) return;

        // back is a mirrored transition, so that the movement reads as a return
        Switch(target, Mirror(Transition));
    }

    private Page? FindPage(string name)
    {
        foreach (UIElement child in Children)
            if (child is Page page && page.Name == name)
                return page;

        return null;
    }

    private static PageTransition Mirror(PageTransition transition) => transition switch
    {
        PageTransition.SlideLeft => PageTransition.SlideRight,
        PageTransition.SlideRight => PageTransition.SlideLeft,
        PageTransition.SlideUp => PageTransition.SlideDown,
        PageTransition.SlideDown => PageTransition.SlideUp,
        _ => transition,
    };

    private Rectangle _baseSlot;

    /// <summary>Bring an unfinished transition to its end.</summary>
    /// <remarks>
    /// A new animation with the same key does displace the old one, and the old
    /// one's completed does run — but only when the new one is added, and by then
    /// Switch has already written the new outgoing page into _outgoing. The old
    /// completed would finish the new transition, and the old outgoing page would
    /// stay visible and shifted forever. So the previous transition is closed
    /// explicitly, before Switch touches any state.
    /// </remarks>
    private void FinishTransition()
    {
        if (_outgoing is not Page outgoing) return;

        // the state is reset first: StopAnimation calls Cancel, which calls
        // completed, and that comes back here. A nulled _outgoing cuts off the
        // re-entry on the first line, and the reference captured by the pattern
        // match doesn't suffer from it
        _outgoing = null;
        _progress = 1f;
        _activeTransition = PageTransition.None;

        this.StopAnimation("page");

        outgoing.IsVisible = false;
        outgoing.Opacity = 1f;
        outgoing.Position = _baseSlot.Position;
    }

    private void Switch(Page target, PageTransition transition)
    {
        FinishTransition();

        Page? previous = _current;

        // without a window the frame tick doesn't run: the animation won't finish
        // and the outgoing page would hang over the new one
        bool canAnimate = transition != PageTransition.None
            && TransitionDurationMs > 0
            && previous is not null
            && FindOwner()?.PlatformWindow is not null
            // a page change is motion for the sake of looks: with reduced motion
            // the new page simply appears in place of the old one
            && !Motion.IsReduced;

        // the transition state is set before IsVisible. Its setter may lead to
        // a layout right away — whenever something lays out on the invalidation,
        // as Win32 and X11 once did by painting inside Invalidate — and
        // ArrangeContentOverride restores the offsets only when _outgoing and
        // _progress are filled in: otherwise both pages would end up in one slot,
        // and that frame would get drawn
        if (canAnimate)
        {
            _outgoing = previous;
            _progress = 0f;
            _activeTransition = transition;
        }

        previous?.RaiseDisappearing();

        _current = target;
        target.RaiseAppearing();
        target.IsVisible = true;

        Navigated?.Invoke(this, target);

        // the page is visible now — its first field gets the focus, as on the
        // form's first show. Before the form is shown there is no owner yet,
        // and Form.Show does it then
        if (FocusOnNavigate)
            FindOwner()?.FocusFirstTextInput(target);

        if (!canAnimate)
        {
            if (previous is not null)
            {
                previous.IsVisible = false;
                previous.Opacity = 1f;
            }

            target.Opacity = 1f;
            Invalidate();

            SchedulePreload(PreloadDelayMs);

            return;
        }

        // layout is deferred until the frame, and the very first frame of the
        // transition shifts the pages from their slot — so the slot must be
        // computed now. Invalidate doesn't fit: it only marks
        FindOwner()?.UpdateLayout();

        Page outgoing = previous!;

        this.Animate("page", 0f, 1f, TimeSpan.FromMilliseconds(TransitionDurationMs),
            Interpolators.Float,
            value =>
            {
                _progress = value;
                ApplyTransition(target, outgoing);
                InvalidateVisual();
            },
            Easing.EaseInOut,
            completed: () =>
            {
                FinishTransition();
                target.Opacity = 1f;

                Invalidate();
            });
        // not during the transition: building a page in the middle of an animation
        // would eat exactly the frames it shows
        SchedulePreload(TransitionDurationMs + PreloadDelayMs);
    }

    /// <summary>Shifts and tints the pages by the current progress.
    /// Changes only Position and Opacity — a full layout per frame isn't needed.</summary>
    private void ApplyTransition(Page incoming, Page outgoing)
    {
        if (_activeTransition == PageTransition.Fade)
        {
            incoming.Opacity = _progress;
            outgoing.Opacity = 1f - _progress;
            return;
        }

        (float dxIn, float dyIn) = OffsetDelta(incoming: true);
        (float dxOut, float dyOut) = OffsetDelta(incoming: false);

        incoming.Position = new Point(_baseSlot.X + dxIn, _baseSlot.Y + dyIn);
        outgoing.Position = new Point(_baseSlot.X + dxOut, _baseSlot.Y + dyOut);
    }

    private (float Dx, float Dy) OffsetDelta(bool incoming)
    {
        float t = incoming ? 1f - _progress : -_progress;

        return _activeTransition switch
        {
            PageTransition.SlideLeft => (_baseSlot.Width * t, 0f),
            PageTransition.SlideRight => (-_baseSlot.Width * t, 0f),
            PageTransition.SlideUp => (0f, _baseSlot.Height * t),
            PageTransition.SlideDown => (0f, -_baseSlot.Height * t),
            _ => (0f, 0f),
        };
    }

    private PageTransition _activeTransition = PageTransition.None;

    // ===== layout =====

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        float width = 0, height = 0;

        // only the visible ones are measured: hidden pages
        // must not affect the container's size
        foreach (UIElement child in Children)
        {
            if (!child.IsVisible) continue;

            child.Measure(inner);

            width = Math.Max(width, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return ResolveSize(
            new Size(width + Padding.Horizontal, height + Padding.Vertical),
            availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        var area = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, contentSize.Width - Padding.Horizontal),
                Math.Max(0, contentSize.Height - Padding.Vertical)));

        // remember the base slot: the animation moves pages relative to it
        _baseSlot = area;

        foreach (UIElement child in Children)
        {
            if (!child.IsVisible) continue;

            child.Arrange(area);
        }

        // if the layout happened in the middle of a transition, restore the offsets
        if (_progress < 1f && _outgoing is not null && _current is not null)
            ApplyTransition(_current, _outgoing);
    }
}