using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls.Navigation;

/// <summary>
/// One view inside a <see cref="PageControl"/> or a <see cref="NavigationHost"/>. The
/// content is created once and survives switches — typed text and scroll position are kept.
/// </summary>
/// <remarks>
/// In a <see cref="NavigationHost"/> a page is one entry of a stack: it hears
/// <see cref="OnNavigatedTo"/> and <see cref="OnNavigatedFrom"/>, may refuse to be left
/// (<see cref="CanLeaveAsync"/>), gives an <see cref="AppBar"/> its title and its
/// <see cref="Actions"/>, and may hand a result back to whoever pushed it
/// (<see cref="ReturnAsync"/>).
/// </remarks>
public class Page : DecoratedWrapControl
{
    private static readonly IReadOnlyDictionary<string, string> NoParameters =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private Func<UIElement>? _factory;
    private bool _built;

    public Page() { }

    /// <summary>A page of ready content.</summary>
    public Page(UIElement content, string? title = null)
    {
        Child = content;
        Title = title;
    }

    /// <summary>The title: an <see cref="AppBar"/> over a navigation stack shows the
    /// current page's.</summary>
    public string? Title
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            TitleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary><see cref="Title"/> changed: an app bar showing it follows.</summary>
    public event EventHandler? TitleChanged;

    public string? IconPathData { get; set; }

    /// <summary>The route the page was made for by a <see cref="NavigationHost"/>
    /// (<see cref="NavigationHost.GoToAsync"/>); in a browser it is the address's part
    /// after the <c>#</c>. Set it on a page pushed by hand to give it an address too.</summary>
    public string? Route { get; set; }

    /// <summary>The query of the route the page was made for: <c>details?id=42</c> gives
    /// <c>id</c> → <c>42</c>. Empty for a page pushed by hand.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; internal set; } = NoParameters;

    /// <summary>The navigation stack the page is in; null — none, or a <see cref="PageControl"/>.</summary>
    public NavigationHost? NavigationHost { get; internal set; }

    /// <summary>The buttons an <see cref="AppBar"/> shows at its end while the page is
    /// the current one, before the bar's own.</summary>
    public System.Collections.ObjectModel.ObservableCollection<AppBarButton> Actions => field ??= [];

    internal bool HasActions => Actions.Count > 0;

    /// <summary>The page became the current one of its stack: pushed, or uncovered by a pop.</summary>
    protected internal virtual void OnNavigatedTo(NavigationEventArgs e) { }

    /// <summary>The page stopped being the current one: covered by a push, or popped.</summary>
    protected internal virtual void OnNavigatedFrom(NavigationEventArgs e) { }

    /// <summary>Asked before the page is popped or replaced — by a back button, the system's
    /// back, a swipe: false keeps it, "the changes aren't saved". Not asked when the stack
    /// goes back to its root in one step from code, nor when a page is pushed over it.</summary>
    protected internal virtual Task<bool> CanLeaveAsync() => Task.FromResult(true);

    /// <summary>The result a <see cref="NavigationHost.PushForResultAsync{TResult}"/> is
    /// waiting for.</summary>
    internal TaskCompletionSource<object?>? ResultSource { get; set; }

    /// <summary>Leave the page with a result for whoever pushed it with
    /// <see cref="NavigationHost.PushForResultAsync{TResult}"/>: the stack goes back.</summary>
    public async Task<bool> ReturnAsync(object? result)
    {
        if (NavigationHost is not { } host || !ReferenceEquals(host.CurrentPage, this))
            return false;

        _pendingResult = result;
        _hasPendingResult = true;

        bool left = await host.PopAsync();

        // kept — CanLeaveAsync said no: the result is not given yet
        if (!left)
        {
            _pendingResult = null;
            _hasPendingResult = false;
        }

        return left;
    }

    private object? _pendingResult;
    private bool _hasPendingResult;

    /// <summary>The page left its stack: whoever waits for its result gets it — the one
    /// given to <see cref="ReturnAsync"/>, or the default when it was left another way.</summary>
    internal void CompleteResult()
    {
        TaskCompletionSource<object?>? source = ResultSource;
        ResultSource = null;

        source?.TrySetResult(_hasPendingResult ? _pendingResult : null);

        _pendingResult = null;
        _hasPendingResult = false;
    }

    /// <summary>Lazy creation: the content is built on first show.</summary>
    public Func<UIElement>? ContentFactory
    {
        get => _factory;
        set
        {
            _factory = value;
            _built = false;
        }
    }

    public event EventHandler? Appearing;
    public event EventHandler? Disappearing;

    /// <summary>The content has already been created. PageControl looks at this
    /// when preparing pages in advance.</summary>
    internal bool IsBuilt => _built;

    internal void EnsureBuilt()
    {
        if (_built || _factory is null) return;

        _built = false;
        try
        {
            Child = _factory();
            _built = true;
        }
        catch
        {
            throw;
        }
    }

    /// <summary>Hot Reload: content made by a factory whose assembly changed is made
    /// again — at once on the page shown, on first show for the others.</summary>
    private protected override void HotReloadCore(HotReloadEventArgs e)
    {
        base.HotReloadCore(e);

        if (!_built || _factory is null || !e.Changed(_factory)) return;

        _built = false;
        Child = null;

        if (IsVisible) EnsureBuilt();
    }

    /// <summary>Under the content: while the page moves in its stack's transition, the
    /// background of what the stack lies on — see
    /// <see cref="Navigation.NavigationHost.TransitionBackdropFor"/>.</summary>
    /// <remarks>Only where the page's own background doesn't cover it already: its own
    /// is drawn again over the backdrop, so a translucent one keeps its tint.</remarks>
    protected override void DrawContent(Graphics g)
    {
        if (NavigationHost?.TransitionBackdropFor(this) is { } backdrop
            && !HasBackgroundGradient
            && CurrentBackground.A < 255)
        {
            Rectangle bounds = g.AlignToPixels(LocalBounds);

            g.FillRoundRectangle(bounds, CornerRadius, backdrop);

            if (CurrentBackground.A > 0)
                FillBackground(g, bounds);
        }

        base.DrawContent(g);
    }

    internal void RaiseAppearing()
    {
        EnsureBuilt();
        Appearing?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseDisappearing() => Disappearing?.Invoke(this, EventArgs.Empty);
}