using ZeppelinForms.Accessibility;
using ZeppelinForms.Animation;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls;

/// <summary>Which way the content is pulled to refresh it.</summary>
public enum RefreshPullDirection : byte
{
    /// <summary>Down from the top: a feed whose newest items come first.</summary>
    TopToBottom,

    /// <summary>Up from the bottom: a chat whose newest messages come last.</summary>
    BottomToTop,
}

/// <summary>Where a <see cref="RefreshContainer"/> is in a refresh.</summary>
public enum RefreshState : byte
{
    /// <summary>Nothing goes on.</summary>
    Idle,

    /// <summary>The content is being pulled, not far enough yet to refresh on release.</summary>
    Interacting,

    /// <summary>Pulled far enough: a release refreshes.</summary>
    Pending,

    /// <summary>The refresh was asked for and has not finished yet.</summary>
    Refreshing,
}

public sealed class RefreshStateChangedEventArgs(RefreshState oldState, RefreshState newState) : EventArgs
{
    public RefreshState OldState { get; } = oldState;

    public RefreshState NewState { get; } = newState;
}

/// <summary>A refresh was asked for — pulled, by F5, or from code.</summary>
/// <remarks>
/// The refresh ends when the handlers return, unless one of them takes a deferral:
/// then it ends when every deferral taken is completed. An asynchronous handler
/// takes one before its first await:
/// <code>
/// container.RefreshRequested += async (_, e) =>
/// {
///     using RefreshDeferral deferral = e.GetDeferral();
///     await feed.LoadNewestAsync();
/// };
/// </code>
/// </remarks>
public sealed class RefreshRequestedEventArgs : EventArgs
{
    private readonly RefreshContainer _owner;
    private readonly int _generation;

    internal RefreshRequestedEventArgs(RefreshContainer owner, int generation)
    {
        _owner = owner;
        _generation = generation;
    }

    /// <summary>Hold the refresh open until the deferral is completed or disposed.</summary>
    public RefreshDeferral GetDeferral()
    {
        _owner.AddDeferral(_generation);

        return new RefreshDeferral(_owner, _generation);
    }
}

/// <summary>Keeps a refresh going. Complete it — or dispose of it — when the new
/// content is in; from any thread.</summary>
public sealed class RefreshDeferral : IDisposable
{
    private readonly RefreshContainer _owner;
    private readonly int _generation;
    private int _completed;

    internal RefreshDeferral(RefreshContainer owner, int generation)
    {
        _owner = owner;
        _generation = generation;
    }

    public void Complete()
    {
        // twice is once: a using around an explicit Complete must not end
        // someone else's deferral
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;

        _owner.CompleteDeferral(_generation);
    }

    public void Dispose() => Complete();
}

/// <summary>
/// Pull to refresh: content pulled past its edge reveals an indicator, and let go
/// far enough, asks for new content and spins until it is in.
/// </summary>
/// <remarks>
/// <para>
/// The content is any element, usually a scrolling list. The pull starts only with
/// the list at the edge it is pulled from; anywhere else the same drag scrolls it.
/// A finger and a pen pull; the mouse doesn't unless <see cref="AllowMousePull"/>
/// says so — on the desktop dragging selects. There F5 refreshes while the focus is
/// inside, and so does <see cref="RequestRefresh"/>.
/// </para>
/// <para>
/// The content moves with the pull rather than the indicator over it, and stays
/// moved aside while the refresh goes on.
/// </para>
/// <para>
/// Pseudo-classes: <c>:refreshing</c> while a refresh goes on.
/// </para>
/// </remarks>
public partial class RefreshContainer : DecoratedWrapControl
{
    private const string OffsetAnimation = "refresh-offset";
    private const string SpinAnimation = "refresh-spin";

    private readonly PanGestureRecognizer _pull;

    /// <summary>How far the content is moved aside now, toward the pull.</summary>
    private float _offset;

    /// <summary>Where the content was laid out; the offset is added to it.</summary>
    private Point _childOrigin;

    /// <summary>The phase of the spinning indicator, 0..1.</summary>
    private float _spin;

    /// <summary>Counts refreshes: a deferral of a refresh that already ended must
    /// not end the next one.</summary>
    private int _generation;

    private int _pendingDeferrals;

    /// <summary>The handlers of the current refresh are still running: a refresh
    /// without deferrals ends only after them.</summary>
    private bool _raising;

    private int _uiThread;

    public RefreshContainer()
    {
        _pull = new PanGestureRecognizer
        {
            Direction = PanDirection.Vertical,
            CanBegin = CanBeginPull,
            CanAccept = CanAcceptPull,
        };

        _pull.Started += (_, e) => OnPull(e);
        _pull.Updated += (_, e) => OnPull(e);
        _pull.Completed += (_, _) => OnPullReleased();
        _pull.Cancelled += (_, _) => OnPullReleased(cancelled: true);

        this.AddGesture(_pull);
    }

    public RefreshContainer(UIElement child) : this()
    {
        Child = child;
    }

    // ===== behavior =====

    public RefreshPullDirection PullDirection
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            ApplyOffset();
        }
    } = RefreshPullDirection.TopToBottom;

    /// <summary>Whether pulling refreshes. F5 and <see cref="RequestRefresh"/> work either way.</summary>
    public bool IsPullEnabled { get; set; } = true;

    /// <summary>Whether a mouse drag pulls too. Off by default: on the desktop
    /// a drag selects text and moves things.</summary>
    public bool AllowMousePull { get; set; }

    /// <summary>How far the content must be pulled for a release to refresh — and
    /// how far it stays moved aside while the refresh goes on.</summary>
    public float RefreshThreshold { get; set; } = 64f;

    public RefreshState State { get; private set; }

    public bool IsRefreshing => State == RefreshState.Refreshing;

    public event EventHandler<RefreshRequestedEventArgs>? RefreshRequested;

    public event EventHandler<RefreshStateChangedEventArgs>? StateChanged;

    // ===== look =====

    /// <summary>The arc of the indicator.</summary>
    [Styled(Category = "Indicator")]
    public partial Color IndicatorColor { get; set; }
    private static Color IndicatorColorDefault => new(255, 0, 120, 215);

    /// <summary>The disc the arc is drawn on.</summary>
    [Styled(Category = "Indicator")]
    public partial Color IndicatorBackground { get; set; }
    private static Color IndicatorBackgroundDefault => Colors.White;

    [Styled(Category = "Indicator")]
    public partial Color IndicatorBorderColor { get; set; }
    private static Color IndicatorBorderColorDefault => new(255, 224, 224, 224);

    /// <summary>The diameter of the indicator.</summary>
    [Styled(Category = "Indicator")]
    public partial float IndicatorSize { get; set; }
    private static float IndicatorSizeDefault => 32f;

    // ===== refreshing =====

    /// <summary>Refresh now, as if the content had been pulled. Nothing happens
    /// while a refresh is already going on.</summary>
    public void RequestRefresh()
    {
        if (IsRefreshing) return;

        BeginRefresh();
    }

    private float Threshold => Math.Max(1f, RefreshThreshold);

    /// <summary>+1 when the pull goes down, −1 when it goes up.</summary>
    private float PullSign => PullDirection == RefreshPullDirection.TopToBottom ? 1f : -1f;

    private void SetState(RefreshState state)
    {
        if (State == state) return;

        RefreshState old = State;
        State = state;

        SetPseudoClass(PseudoClass.Refreshing, state == RefreshState.Refreshing);
        InvalidateVisual();

        StateChanged?.Invoke(this, new RefreshStateChangedEventArgs(old, state));
    }

    private void BeginRefresh()
    {
        _generation++;
        _pendingDeferrals = 0;
        _uiThread = Environment.CurrentManagedThreadId;

        SetState(RefreshState.Refreshing);

        // the indicator rests in the gap while the refresh goes on; it spins even
        // under reduced motion — it says work is going on, not decoration
        AnimateOffset(Threshold);
        this.AnimateLoop(SpinAnimation, TimeSpan.FromSeconds(1), phase =>
        {
            _spin = phase;
            InvalidateVisual();
        });

        FindOwner()?.Announce(Localization.Get(ZfText.Refreshing));

        int generation = _generation;
        _raising = true;

        try
        {
            RefreshRequested?.Invoke(this, new RefreshRequestedEventArgs(this, generation));
        }
        finally
        {
            _raising = false;
        }

        if (generation == _generation && _pendingDeferrals == 0)
            EndRefresh();
    }

    internal void AddDeferral(int generation)
    {
        if (generation == _generation && IsRefreshing)
            _pendingDeferrals++;
    }

    internal void CompleteDeferral(int generation)
    {
        // a deferral completed off the UI thread — after an await without a context,
        // in a thread pool callback — finishes on it. The posted call doesn't ask
        // again: whatever thread runs the form's queue is the UI thread by definition,
        // and asking again would post it once more, and again, forever
        if (Environment.CurrentManagedThreadId != _uiThread && FindOwner() is { } form)
        {
            form.Invoke(() => FinishDeferral(generation));
            return;
        }

        FinishDeferral(generation);
    }

    private void FinishDeferral(int generation)
    {
        if (generation != _generation || !IsRefreshing) return;

        _pendingDeferrals = Math.Max(0, _pendingDeferrals - 1);

        if (_pendingDeferrals == 0 && !_raising)
            EndRefresh();
    }

    private void EndRefresh()
    {
        if (!IsRefreshing) return;

        this.StopAnimation(SpinAnimation);
        SetState(RefreshState.Idle);

        AnimateOffset(0f);

        FindOwner()?.Announce(Localization.Get(ZfText.Refreshed));
    }

    // ===== pulling =====

    private bool CanBeginPull(PointerContact contact) =>
        IsPullEnabled
        && IsEffectivelyEnabled
        && !IsRefreshing
        && (contact.Kind != PointerKind.Mouse || AllowMousePull);

    /// <summary>The pull goes the right way, and everything that scrolls under the
    /// pointer is at the edge it starts from. A scroller in the middle of its content
    /// takes the drag itself — with a finger it gets there first; a mouse, which
    /// scrollers don't take, needs the check here.</summary>
    private bool CanAcceptPull(PointerContact contact, Point travel)
    {
        if (travel.Y * PullSign <= 0) return false;

        foreach (UIElement element in contact.Chain)
        {
            if (ReferenceEquals(element, this) || !IsInside(element, this)) continue;

            if (element is ITouchScrollTarget scroller && !AtPullEdge(scroller))
                return false;
        }

        return true;
    }

    private bool AtPullEdge(ITouchScrollTarget scroller)
    {
        float max = scroller.PanMaxScroll.Y;

        if (max <= 0f) return true;

        float scroll = scroller.PanScroll.Y;

        return PullDirection == RefreshPullDirection.TopToBottom
            ? scroll <= 0.5f
            : scroll >= max - 0.5f;
    }

    /// <summary>Whether the nearest refresh container around a scroller claims the
    /// drag it is about to take: the drag goes the pull's way, and the scroller is
    /// at the edge the pull starts from.</summary>
    internal static bool ClaimsPull(ITouchScrollTarget scroller, PointerContact contact, Point travel)
    {
        for (UIElement? element = scroller.Element.Parent; element is not null; element = element.Parent)
        {
            if (element is not RefreshContainer container) continue;

            return container.CanBeginPull(contact)
                && MathF.Abs(travel.Y) >= MathF.Abs(travel.X)
                && travel.Y * container.PullSign > 0
                && container.AtPullEdge(scroller);
        }

        return false;
    }

    private static bool IsInside(UIElement element, UIElement root)
    {
        for (UIElement? current = element.Parent; current is not null; current = current.Parent)
            if (ReferenceEquals(current, root))
                return true;

        return false;
    }

    /// <summary>How far the content follows a pull: a little slower than the finger
    /// at first, then harder and harder, up to twice the threshold. The threshold
    /// takes about twice its length of pulling: a flick down the list doesn't refresh.</summary>
    private float Resist(float pulled)
    {
        if (pulled <= 0f) return 0f;

        float limit = Threshold * 2f;

        return limit * (1f - MathF.Exp(-0.7f * pulled / limit));
    }

    private void OnPull(PanGestureEventArgs e)
    {
        // the finger has the content now: whatever was settling stops where it is
        this.StopAnimation(OffsetAnimation);

        _offset = Resist(e.TotalOffset.Y * PullSign);
        ApplyOffset();

        SetState(_offset >= Threshold ? RefreshState.Pending : RefreshState.Interacting);
    }

    private void OnPullReleased(bool cancelled = false)
    {
        if (State == RefreshState.Pending && !cancelled)
        {
            BeginRefresh();
            return;
        }

        if (IsRefreshing) return;

        SetState(RefreshState.Idle);
        AnimateOffset(0f);
    }

    private void AnimateOffset(float target)
    {
        if (_offset == target)
        {
            this.StopAnimation(OffsetAnimation);
            return;
        }

        if (FindOwner() is null || Motion.IsReduced)
        {
            this.StopAnimation(OffsetAnimation);

            _offset = target;
            ApplyOffset();
            return;
        }

        this.Animate(OffsetAnimation, _offset, target, TimeSpan.FromMilliseconds(180),
            Interpolators.Float,
            value =>
            {
                _offset = value;
                ApplyOffset();
            },
            Easing.EaseOut);
    }

    /// <summary>Move the content aside by the offset without laying it out again:
    /// only its place changes, as a scrolling panel moves its children.</summary>
    private void ApplyOffset()
    {
        if (Child is { } child)
            child.Position = new Point(_childOrigin.X, _childOrigin.Y + _offset * PullSign);

        InvalidateVisual();
    }

    /// <summary>How far the content is moved aside now.</summary>
    public float PullOffset => _offset;

    // ===== layout =====

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size size = base.ArrangeOverride(finalSize);

        if (Child is { } child)
        {
            _childOrigin = child.Position;

            // a layout pass in the middle of a pull or a refresh keeps the content aside
            if (_offset != 0f) ApplyOffset();
        }

        return size;
    }

    // ===== drawing =====

    /// <summary>The indicator, in the gap the content leaves: in its middle, and
    /// sliding in with the pull until there is room for it whole.</summary>
    internal Rectangle IndicatorBounds
    {
        get
        {
            Rectangle content = ContentBounds;
            float size = IndicatorSize;
            float gap = _offset;

            float centerY = PullDirection == RefreshPullDirection.TopToBottom
                ? content.Y + gap / 2f
                : content.Y + content.Height - gap / 2f;

            return new Rectangle(
                new Point(content.X + (content.Width - size) / 2f, centerY - size / 2f),
                new Size(size, size));
        }
    }

    protected override void DrawDecoration(Graphics g)
    {
        if (_offset <= 0.5f && !IsRefreshing) return;

        Rectangle disc = IndicatorBounds;
        float progress = Math.Clamp(_offset / Threshold, 0f, 1f);

        // the indicator fades in as it comes out from under the edge
        float opacity = IsRefreshing ? 1f : progress;

        g.Save();
        g.ClipRect(ContentBounds);

        if (opacity < 1f) g.SaveLayer(opacity);

        g.FillEllipse(disc, IndicatorBackground);

        if (IndicatorBorderColor.A > 0)
            g.DrawEllipse(disc, IndicatorBorderColor, 1f);

        float stroke = Math.Max(1.5f, disc.Width / 12f);
        float inset = disc.Width * 0.28f;

        var arc = new Rectangle(
            new Point(disc.X + inset, disc.Y + inset),
            new Size(disc.Width - inset * 2f, disc.Height - inset * 2f));

        if (IsRefreshing)
        {
            // a spinning three-quarter arc: work going on
            g.DrawArc(arc, _spin * 360f - 90f, 270f, IndicatorColor, stroke);
        }
        else
        {
            // the arc grows with the pull and closes when a release would refresh;
            // its start turns along, so the pull reads as winding something up
            float sweep = State == RefreshState.Pending ? 359.9f : 300f * progress;

            if (sweep > 0.5f)
                g.DrawArc(arc, -90f + 180f * progress, sweep, IndicatorColor, stroke);
        }

        if (opacity < 1f) g.Restore();

        g.Restore();
    }

    // ===== keyboard =====

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // F5 refreshes what has the focus, as in a browser
        if (e.Key == Key.F5 && e.Modifiers == KeyModifiers.None && IsEffectivelyEnabled)
        {
            RequestRefresh();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}