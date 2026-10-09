using System.ComponentModel;
using ZeppelinForms.Animation;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>How a <see cref="SplitView"/> shows its pane.</summary>
public enum SplitViewDisplayMode : byte
{
    /// <summary>Hidden while closed; opens over the content.</summary>
    Overlay,

    /// <summary>Hidden while closed; opens beside the content and pushes it aside.</summary>
    Inline,

    /// <summary>A strip of the pane stays while closed; it opens over the content.</summary>
    CompactOverlay,

    /// <summary>A strip of the pane stays while closed; it opens beside the content.</summary>
    CompactInline,
}

/// <summary>The side of a <see cref="SplitView"/> its pane is on.</summary>
public enum SplitViewPanePlacement : byte
{
    /// <summary>Where lines start: the left edge, the right one under right-to-left.</summary>
    Start,

    /// <summary>Where lines end: the right edge, the left one under right-to-left.</summary>
    End,
}

/// <summary>
/// A pane beside the content that opens and closes: navigation, a list of
/// folders, settings. Closed, it is hidden or kept as a strip of icons; open, it
/// lies over the content or pushes it aside, as <see cref="DisplayMode"/> says.
/// </summary>
/// <remarks>
/// <para>
/// The pane is laid out at its open width all the time and shown through a window
/// of the current width: while it opens and closes nothing in it reflows, and the
/// compact strip is the edge of the same pane — the column of icons the open pane
/// labels.
/// </para>
/// <para>
/// A pane over the content is dismissed the way a flyout is: a click on the
/// content, or Escape. It takes the focus when it opens, if the focus was in the
/// split view, and gives it back when it closes.
/// </para>
/// <para>
/// Pseudo-classes: <c>:open</c> while the pane is open, <c>:compact</c> and
/// <c>:overlay</c> by the display mode.
/// </para>
/// </remarks>
public partial class SplitView : DecoratedPanel
{
    private readonly SplitViewPaneHost _host;
    private readonly SplitViewScrim _scrim;

    private UIElement? _content;

    private bool _isPaneOpen;

    /// <summary>How far the pane has opened: 0 — closed, 1 — open, between while it moves.</summary>
    private float _progress;

    /// <summary>The element that had the focus before the pane took it. It gets
    /// the focus back when the pane closes.</summary>
    private UIElement? _focusBeforeOpen;

    public SplitView()
    {
        _host = new SplitViewPaneHost(this);
        _scrim = new SplitViewScrim(this);

        // drawn in this order: the content, the scrim over it, the pane on top.
        // The content goes in at the front when it is set
        Children.Add(_scrim);
        Children.Add(_host);

        SyncParts();
    }

    // ===== content =====

    /// <summary>What the pane shows.</summary>
    public UIElement? Pane
    {
        get => _host.Child;
        set
        {
            if (ReferenceEquals(_host.Child, value)) return;

            _host.Child = value;

            SyncParts();
            Invalidate();
        }
    }

    /// <summary>The main area.</summary>
    public UIElement? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value)) return;

            if (_content is not null)
                Children.Remove(_content);

            _content = value;

            // under the scrim and the pane: an open pane lies over it
            if (value is not null)
                Children.Insert(0, value);

            Invalidate();
        }
    }

    /// <summary>The pane is read and tabbed through before the content, wherever
    /// it is drawn: it is the navigation the content is chosen with.</summary>
    protected internal override IEnumerable<UIElement> NavigationOrder
    {
        get
        {
            yield return _host;

            foreach (UIElement child in Children)
                if (!ReferenceEquals(child, _host) && !ReferenceEquals(child, _scrim))
                    yield return child;
        }
    }

    /// <summary>The name the pane is read by. "Pane" when not set.</summary>
    public string? PaneTitle { get; set; }

    // ===== state =====

    public SplitViewDisplayMode DisplayMode
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            SyncParts();
            Invalidate();
        }
    } = SplitViewDisplayMode.Overlay;

    public SplitViewPanePlacement PanePlacement
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = SplitViewPanePlacement.Start;

    /// <summary>Whether the pane is open. Setting it plays the opening or the
    /// closing; <see cref="PaneClosing"/> may refuse the closing.</summary>
    public bool IsPaneOpen
    {
        get => _isPaneOpen;
        set
        {
            if (_isPaneOpen == value) return;

            if (value)
            {
                PaneOpening?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                var closing = new CancelEventArgs();
                PaneClosing?.Invoke(this, closing);

                if (closing.Cancel) return;
            }

            _isPaneOpen = value;

            SetPseudoClass(PseudoClass.Open, value);
            SyncParts();

            if (value) TakeFocus();
            else ReturnFocus();

            AnimatePane();

            IsPaneOpenChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether a click on the content and Escape close a pane that lies
    /// over the content. A pane beside the content stays until it is closed.</summary>
    public bool IsLightDismissEnabled
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            SyncParts();
        }
    } = true;

    /// <summary>How long the pane takes to open or close, in milliseconds.
    /// Instant when the system asks for reduced motion.</summary>
    public int PaneTransitionDurationMs { get; set; } = 200;

    public void OpenPane() => IsPaneOpen = true;

    public void ClosePane() => IsPaneOpen = false;

    public void TogglePane() => IsPaneOpen = !IsPaneOpen;

    /// <summary>The pane starts to open.</summary>
    public event EventHandler? PaneOpening;

    /// <summary>The pane has opened: the opening has played out.</summary>
    public event EventHandler? PaneOpened;

    /// <summary>The pane is about to close. Cancel keeps it open.</summary>
    public event EventHandler<CancelEventArgs>? PaneClosing;

    /// <summary>The pane has closed: the closing has played out.</summary>
    public event EventHandler? PaneClosed;

    /// <summary><see cref="IsPaneOpen"/> changed — at once, before the pane moves.</summary>
    public event EventHandler? IsPaneOpenChanged;

    // ===== look =====

    /// <summary>The width of the open pane.</summary>
    [Styled(Category = "Pane", AffectsLayout = true)]
    public partial float OpenPaneLength { get; set; }
    private static float OpenPaneLengthDefault => 320f;

    /// <summary>The width of the strip a compact pane keeps while closed.</summary>
    [Styled(Category = "Pane", AffectsLayout = true)]
    public partial float CompactPaneLength { get; set; }
    private static float CompactPaneLengthDefault => 48f;

    [Styled(Category = "Pane")]
    public partial Color PaneBackground { get; set; }
    private static Color PaneBackgroundDefault => new(255, 243, 243, 243);

    /// <summary>The line between the pane and the content.</summary>
    [Styled(Category = "Pane")]
    public partial Color PaneBorderColor { get; set; }
    private static Color PaneBorderColorDefault => new(255, 224, 224, 224);

    /// <summary>The tint over the content while a pane lies over it.</summary>
    [Styled(Category = "Pane")]
    public partial Color ScrimColor { get; set; }
    private static Color ScrimColorDefault => new(51, 0, 0, 0);

    // ===== geometry =====

    internal bool IsCompact => DisplayMode is SplitViewDisplayMode.CompactOverlay or SplitViewDisplayMode.CompactInline;

    internal bool IsOverlay => DisplayMode is SplitViewDisplayMode.Overlay or SplitViewDisplayMode.CompactOverlay;

    /// <summary>The pane is against the right edge on screen: at the end in
    /// left-to-right, at the start in right-to-left. For what the panel doesn't
    /// mirror by itself — the pane inside its window, the edge line.</summary>
    internal bool PaneAtRight => (PanePlacement == SplitViewPanePlacement.End) != IsRightToLeft;

    internal float OpenProgress => _progress;

    private float ClosedLength => IsCompact ? Math.Max(0f, CompactPaneLength) : 0f;

    /// <summary>How wide the pane is now: from the closed strip to the open width.</summary>
    internal float CurrentPaneLength
    {
        get
        {
            float closed = ClosedLength;
            float open = Math.Max(closed, OpenPaneLength);

            return closed + (open - closed) * _progress;
        }
    }

    /// <summary>The width the content gives up to the pane: a pane over the content
    /// takes only its closed strip, a pane beside it all it has.</summary>
    private float ReservedLength => IsOverlay ? ClosedLength : CurrentPaneLength;

    /// <summary>The pane, in the split view's coordinates.</summary>
    public Rectangle PaneBounds => new(_host.Position, _host.ActualSize);

    // ===== layout =====

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        float reserved = ReservedLength;

        var contentAvailable = new Size(
            float.IsFinite(inner.Width) ? Math.Max(0, inner.Width - reserved) : inner.Width,
            inner.Height);

        _content?.Measure(contentAvailable);
        _host.Measure(new Size(CurrentPaneLength, inner.Height));
        _scrim.Measure(contentAvailable);

        Size content = _content?.DesiredSize ?? Size.Empty;
        Size pane = _host.DesiredSize;

        // a split view fills what it is given; only along an unbounded axis does it
        // fall back to what its parts need
        return ResolveSize(
            new Size(
                (float.IsFinite(inner.Width) ? inner.Width : content.Width + reserved) + Padding.Horizontal,
                (float.IsFinite(inner.Height) ? inner.Height : Math.Max(content.Height, pane.Height)) + Padding.Vertical),
            availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        var area = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, contentSize.Width - Padding.Horizontal),
                Math.Max(0, contentSize.Height - Padding.Vertical)));

        float pane = Math.Min(CurrentPaneLength, area.Width);
        float reserved = Math.Min(ReservedLength, area.Width);

        // in the reading direction: the panel mirrors its children under
        // right-to-left by itself, and the start lands on the right there
        bool right = PanePlacement == SplitViewPanePlacement.End;

        var content = new Rectangle(
            new Point(right ? area.X : area.X + reserved, area.Y),
            new Size(area.Width - reserved, area.Height));

        _content?.Arrange(content);

        // the scrim lies over the content only: the closed strip is the pane's
        _scrim.Arrange(content);

        _host.Arrange(new Rectangle(
            new Point(right ? area.X + area.Width - pane : area.X, area.Y),
            new Size(pane, area.Height)));
    }

    // ===== opening and closing =====

    private void AnimatePane()
    {
        float target = _isPaneOpen ? 1f : 0f;

        if (_progress == target)
        {
            FinishMove();
            return;
        }

        int duration = (int)(PaneTransitionDurationMs * Math.Abs(target - _progress));

        if (FindOwner() is null || duration <= 0 || Motion.IsReduced)
        {
            // a pane already moving keeps no animation running toward the old side
            this.StopAnimation("pane");

            _progress = target;
            ApplyProgress();
            FinishMove();
            return;
        }

        this.Animate("pane", _progress, target, TimeSpan.FromMilliseconds(duration),
            Interpolators.Float,
            value =>
            {
                _progress = value;
                ApplyProgress();
            },
            Easing.EaseOut,
            completed: FinishMove);
    }

    private void ApplyProgress()
    {
        SyncParts();

        // the pane's width changes, and beside the content the content's as well
        Invalidate();
    }

    /// <summary>The move played out. The completion of a move reversed halfway
    /// comes too, and must not report the side it never reached.</summary>
    private void FinishMove()
    {
        float target = _isPaneOpen ? 1f : 0f;

        if (_progress != target) return;

        SyncParts();

        if (_isPaneOpen) PaneOpened?.Invoke(this, EventArgs.Empty);
        else PaneClosed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Bring the pane, the scrim and the pseudo-classes in line with the
    /// state: a pane closed to nothing is hidden — from Tab and the reader too — and
    /// the scrim is there only while a pane lies over the content.</summary>
    private void SyncParts()
    {
        _host.IsVisible = _host.Child is not null && (IsCompact || _isPaneOpen || _progress > 0f);

        _scrim.IsVisible = IsOverlay && IsLightDismissEnabled && _host.Child is not null
            && (_isPaneOpen || _progress > 0f);

        SetPseudoClass(PseudoClass.Compact, IsCompact);
        SetPseudoClass(PseudoClass.Overlay, IsOverlay);
    }

    /// <summary>A pane opening over the content takes the focus, as a flyout does —
    /// but only from inside the split view: a pane opened from elsewhere must not
    /// pull the user away from what they are doing.</summary>
    private void TakeFocus()
    {
        _focusBeforeOpen = null;

        if (!IsOverlay || _host.Child is null || FindOwner() is not { } form) return;

        if (form.FocusedElement is not { } focused) return;

        if (!IsInside(focused, this) || IsInside(focused, _host)) return;

        _focusBeforeOpen = focused;
        form.FocusFirstStop(_host);
    }

    /// <summary>The pane closes: the focus inside it goes back to where it came
    /// from, before the pane is hidden with the focus still in it.</summary>
    private void ReturnFocus()
    {
        UIElement? previous = _focusBeforeOpen;
        _focusBeforeOpen = null;

        if (FindOwner() is not { } form) return;

        if (form.FocusedElement is not { } focused || !IsInside(focused, _host)) return;

        if (previous is not null && previous.IsEffectivelyVisible)
            form.FocusElement(previous);
    }

    private static bool IsInside(UIElement element, UIElement root)
    {
        for (UIElement? current = element; current is not null; current = current.Parent)
            if (ReferenceEquals(current, root))
                return true;

        return false;
    }

    /// <summary>A click on the content, past a pane lying over it.</summary>
    internal void LightDismiss()
    {
        if (_isPaneOpen && IsOverlay && IsLightDismissEnabled)
            IsPaneOpen = false;
    }

    // ===== input =====

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.Modifiers == KeyModifiers.None
            && _isPaneOpen && IsOverlay && IsLightDismissEnabled)
        {
            IsPaneOpen = false;
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}

/// <summary>The window the pane is seen through: as wide as the pane is open,
/// with the pane laid out inside at its full width and anchored to the outer edge.</summary>
internal sealed class SplitViewPaneHost : DecoratedWrapControl
{
    private readonly SplitView _owner;

    public SplitViewPaneHost(SplitView owner)
    {
        _owner = owner;
    }

    internal SplitView View => _owner;

    protected override Size MeasureOverride(Size availableSize)
    {
        // always at the open width: nothing reflows while the pane moves
        Child?.Measure(new Size(Math.Max(0, _owner.OpenPaneLength), availableSize.Height));

        Size child = Child?.DesiredSize ?? Size.Empty;

        return ResolveSize(
            new Size(
                float.IsFinite(availableSize.Width) ? availableSize.Width : child.Width,
                float.IsFinite(availableSize.Height) ? availableSize.Height : child.Height),
            availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        float open = Math.Max(finalSize.Width, _owner.OpenPaneLength);

        // the outer edge shows first: a compact strip is the column of icons there
        float x = _owner.PaneAtRight ? finalSize.Width - open : 0f;

        Child?.Arrange(new Rectangle(new Point(x, 0), new Size(open, finalSize.Height)));

        return finalSize;
    }

    protected override void DrawContent(Graphics g)
    {
        if (_owner.PaneBackground.A > 0)
            g.FillRectangle(LocalBounds, _owner.PaneBackground);
    }

    protected override void DrawDecoration(Graphics g)
    {
        Color edge = _owner.PaneBorderColor;

        if (edge.A == 0 || ActualSize.Width <= 0) return;

        // the inner edge, against the content
        float x = _owner.PaneAtRight ? 0f : ActualSize.Width - 1f;

        g.FillRectangle(new Rectangle(new Point(x, 0), new Size(1f, ActualSize.Height)), edge);
    }
}

/// <summary>The tint over the content while a pane lies over it; a click on it
/// dismisses the pane.</summary>
internal sealed class SplitViewScrim : UnitControl
{
    private readonly SplitView _owner;

    public SplitViewScrim(SplitView owner)
    {
        _owner = owner;

        // it covers the content area whole, not centered in it as a leaf control
        SetControlDefault(HorizontalAlignmentProperty, Enums.HorizontalAlignment.Stretch);
        SetControlDefault(VerticalAlignmentProperty, Enums.VerticalAlignment.Stretch);

        // decoration: the reader has the pane and the content, not this
        IsAccessibilityHidden = true;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(float.IsFinite(availableSize.Width) ? availableSize.Width : 0f,
            float.IsFinite(availableSize.Height) ? availableSize.Height : 0f);

    public override void Draw(Graphics g)
    {
        Color color = _owner.ScrimColor;

        if (color.A == 0) return;

        // the tint comes and goes with the pane
        byte alpha = (byte)Math.Round(color.A * Math.Clamp(_owner.OpenProgress, 0f, 1f));

        if (alpha > 0)
            g.FillRectangle(LocalBounds, new Color(alpha, color.R, color.G, color.B));
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        _owner.LightDismiss();
        e.Handled = true;
    }
}