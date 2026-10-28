using System.ComponentModel;
using ZeppelinForms.Animation;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls.Navigation;

/// <summary>How a <see cref="NavigationHost"/>'s stack changed.</summary>
public enum NavigationKind : byte
{
    /// <summary>A page was pushed over the current one.</summary>
    Push,

    /// <summary>The current page was popped — or several, back to the root.</summary>
    Pop,

    /// <summary>The current page was replaced by another.</summary>
    Replace,

    /// <summary>The stack was started anew from a root page.</summary>
    Root,
}

/// <summary>A <see cref="NavigationHost"/>'s stack changed: <see cref="To"/> is the current page now.</summary>
public sealed class NavigationEventArgs(NavigationKind kind, Page? from, Page to) : EventArgs
{
    public NavigationKind Kind { get; } = kind;

    /// <summary>The page that was the current one; null for the first root.</summary>
    public Page? From { get; } = from;

    public Page To { get; } = to;
}

/// <summary>A <see cref="NavigationHost"/>'s stack is about to change; cancel to keep it.</summary>
public sealed class NavigatingEventArgs(NavigationKind kind, Page? from, Page to) : CancelEventArgs
{
    public NavigationKind Kind { get; } = kind;

    public Page? From { get; } = from;

    /// <summary>The page that will be the current one.</summary>
    public Page To { get; } = to;
}

/// <summary>
/// A stack of pages, as a phone application goes through its screens: a page pushed over
/// the current one slides in, going back slides it away and uncovers the page under it as
/// it was left. The stack goes back by itself on the system's back — Android's button and
/// gesture, the browser's back button, Alt+Left — and on a swipe from the left edge.
/// </summary>
/// <remarks>
/// <para>
/// Pages are made by hand (<see cref="PushAsync(Page, bool)"/>) or by route
/// (<see cref="RegisterRoute(string, Func{Page})"/>, <see cref="GoToAsync"/>): a route
/// is what a browser shows after the <c>#</c>, and a page opened at such an address is
/// built by its routes — a deep link.
/// </para>
/// <para>
/// An <see cref="AppBar"/> given the host shows the current page's title, a back button
/// while there is somewhere to go back to, and the page's <see cref="Page.Actions"/>.
/// </para>
/// <para>
/// Pages under the current one stay in the tree, hidden: their text, scroll and state are
/// there when they are uncovered. A popped page leaves the tree.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var host = new NavigationHost { RootPage = new Page(new HomeView(), "Orders") };
/// host.RegisterRoute("order", p => new OrderPage(int.Parse(p["id"])));
///
/// await host.GoToAsync("order?id=42");
/// await host.PopAsync();
/// </code>
/// </example>
public class NavigationHost : DecoratedPanel, IBackHandler
{
    private readonly List<Page> _stack = [];
    private readonly Dictionary<string, Func<IReadOnlyDictionary<string, string>, Page>> _routes = new(StringComparer.Ordinal);
    private readonly PanGestureRecognizer _swipeBack;

    private Form? _form;
    private INavigationHistory? _history;

    /// <summary>Who pushed each entry of a window's history, the latest last: several stacks
    /// share it — the destinations of a <see cref="Scaffold"/> —, and the browser's back
    /// belongs to the one that pushed the entry it took, shown or not.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<INavigationHistory, List<NavigationHost>> HistoryOwners = new();

    /// <summary>How many entries of the browser's history this stack pushed and hasn't
    /// gone back over: it never goes back further than that — further is the pages the
    /// browser showed before the application.</summary>
    private int _historyDepth;

    // ===== the transition in progress =====

    private Page? _incoming;
    private Page? _outgoing;
    private PageTransition _activeTransition = PageTransition.None;

    /// <summary>A pop: the outgoing page is the top one and moves away over the incoming.</summary>
    private bool _transitionIsPop;

    /// <summary>0 — the transition's start, 1 — its end.</summary>
    private float _progress = 1f;

    private Action? _transitionDone;
    private Rectangle _slot;

    /// <summary>The finger is moving the top page: the pop follows it until it is let go.</summary>
    private bool _dragging;

    public NavigationHost()
    {
        // from the left edge, as iOS and Android's predictive back: the page follows the
        // finger and goes when it is let go far enough or fast enough
        _swipeBack = this.AddGesture(new PanGestureRecognizer
        {
            Direction = PanDirection.Horizontal,
            CanBegin = CanBeginSwipeBack,
        });

        _swipeBack.Started += OnSwipeStarted;
        _swipeBack.Updated += OnSwipeUpdated;
        _swipeBack.Completed += OnSwipeCompleted;
        _swipeBack.Cancelled += OnSwipeCancelled;
    }

    // ===== the stack =====

    /// <summary>The pages, the root first and the current one last.</summary>
    public IReadOnlyList<Page> Stack => _stack;

    public Page? CurrentPage => _stack.Count > 0 ? _stack[^1] : null;

    /// <summary>The first page. Setting it starts the stack anew, without a transition:
    /// the pages over the old root are dropped.</summary>
    public Page? RootPage
    {
        get => _stack.Count > 0 ? _stack[0] : null;
        set
        {
            if (value is null) throw new ArgumentNullException(nameof(value), "A navigation stack needs a root page.");

            SetRoot(value);
        }
    }

    /// <summary>Whether there is a page to go back to.</summary>
    public bool CanGoBack => _stack.Count > 1;

    /// <summary>How a page comes in on a push; a pop goes the mirrored way. Horizontal
    /// slides by default, as on a phone.</summary>
    public PageTransition Transition { get; set; } = PageTransition.SlideLeft;

    public int TransitionDurationMs { get; set; } = 250;

    /// <summary>Give the first text field of a page the focus when it is shown, as a form
    /// does on its first show. False by default: on a phone that opens the keyboard over
    /// every page.</summary>
    public bool FocusOnNavigate { get; set; }

    /// <summary>Whether a swipe from the left edge goes back. True by default; a page
    /// whose content is swiped from the edge itself — a drawing surface — turns it off.</summary>
    public bool SwipeBackEnabled { get; set; } = true;

    /// <summary>How wide the strip at the left edge a swipe back starts in is.</summary>
    public float SwipeBackEdgeWidth { get; set; } = 24f;

    /// <summary>Whether the stack goes back on the system's back — Android's, the
    /// browser's, Alt+Left. True by default.</summary>
    public bool HandlesBackRequests { get; set; } = true;

    /// <summary>Whether pushes and pops become entries of the browser's history, so its
    /// back and forward buttons and the address follow the stack, and a page opened at an
    /// address with a route is built by it. True by default; only in a browser.</summary>
    public bool TracksBrowserHistory { get; set; } = true;

    /// <summary>The stack is about to change; cancel to keep it.</summary>
    public event EventHandler<NavigatingEventArgs>? Navigating;

    /// <summary>The stack changed: the current page is another, and the transition is done.</summary>
    public event EventHandler<NavigationEventArgs>? Navigated;

    /// <summary>Start the stack anew from <paramref name="root"/>, without a transition.</summary>
    public void SetRoot(Page root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (_stack.Count == 1 && ReferenceEquals(_stack[0], root)) return;

        FinishTransition();

        Page? previous = CurrentPage;

        foreach (Page page in _stack.ToArray())
            RemovePage(page);

        _stack.Clear();

        AddPage(root);
        _stack.Add(root);
        root.IsVisible = true;
        root.RaiseAppearing();

        var args = new NavigationEventArgs(NavigationKind.Root, previous, root);
        root.OnNavigatedTo(args);
        Navigated?.Invoke(this, args);

        // the entries of the dropped pages stay in the browser's history, which has no
        // way to forget them: the address at least names the new root, and the entries
        // are nobody's — the browser's back over them goes to a stack that is seen
        ForgetEntries(_historyDepth);
        _history?.Replace(Location());

        Invalidate();
    }

    /// <summary>Push <paramref name="page"/> over the current one. The task ends when
    /// the page is in place.</summary>
    public Task PushAsync(Page page, bool animated = true)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (_stack.Contains(page))
            throw new InvalidOperationException("The page is in this navigation stack already.");

        if (page.NavigationHost is not null || page.Parent is not null)
            throw new InvalidOperationException("The page belongs to another container. A page is in one stack at a time.");

        if (CurrentPage is null)
        {
            SetRoot(page);
            return Task.CompletedTask;
        }

        Page from = CurrentPage;

        if (!RaiseNavigating(NavigationKind.Push, from, page)) return Task.CompletedTask;

        AddPage(page);
        _stack.Add(page);

        PushHistory();

        return Switch(from, page, NavigationKind.Push, isPop: false, animated);
    }

    /// <summary>Push a page of <paramref name="content"/>.</summary>
    public Task PushAsync(UIElement content, string? title = null, bool animated = true) =>
        PushAsync(new Page(content, title), animated);

    /// <summary>Push <paramref name="page"/> and wait until it is left: the result it
    /// gives to <see cref="Page.ReturnAsync"/>, or the default when it was left another way.</summary>
    public async Task<TResult?> PushForResultAsync<TResult>(Page page, bool animated = true)
    {
        ArgumentNullException.ThrowIfNull(page);

        var result = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        page.ResultSource = result;

        await PushAsync(page, animated);

        // not pushed — a Navigating handler said no: there will be no result
        if (!_stack.Contains(page))
        {
            page.ResultSource = null;
            return default;
        }

        object? value = await result.Task;

        return value is TResult typed ? typed : default;
    }

    /// <summary>Go back to the previous page. False — there is none, or the current page
    /// kept itself (<see cref="Page.CanLeaveAsync"/>), or a handler of
    /// <see cref="Navigating"/> cancelled.</summary>
    public Task<bool> PopAsync(bool animated = true) => PopCoreAsync(animated, BackSource.Code);

    private async Task<bool> PopCoreAsync(bool animated, BackSource source)
    {
        if (!CanGoBack) return false;

        Page from = _stack[^1];
        Page to = _stack[^2];

        if (!await from.CanLeaveAsync() || !ReferenceEquals(CurrentPage, from) || !RaiseNavigating(NavigationKind.Pop, from, to))
        {
            // the browser went back already: its entry comes back, so the address and
            // the history keep matching the page still shown
            if (source == BackSource.Browser) _history?.Push(Location());

            return false;
        }

        _stack.RemoveAt(_stack.Count - 1);

        // the browser's own back moved its history already
        if (source == BackSource.Browser) ForgetEntries(1);
        else BackHistory(1);

        await Switch(from, to, NavigationKind.Pop, isPop: true, animated);
        return true;
    }

    /// <summary>Go back to the root page in one step: the pages between are dropped
    /// without being asked.</summary>
    public async Task PopToRootAsync(bool animated = true)
    {
        if (!CanGoBack) return;

        Page from = _stack[^1];
        Page root = _stack[0];

        if (!RaiseNavigating(NavigationKind.Pop, from, root)) return;

        int dropped = _stack.Count - 1;

        // the pages between go at once: only the current one is seen leaving
        for (int i = _stack.Count - 2; i >= 1; i--)
        {
            Page between = _stack[i];
            _stack.RemoveAt(i);
            RemovePage(between);
        }

        _stack.RemoveAt(_stack.Count - 1);
        BackHistory(dropped);

        await Switch(from, root, NavigationKind.Pop, isPop: true, animated);
    }

    /// <summary>Put <paramref name="page"/> in place of the current one: the stack keeps
    /// its depth — a sign-in page replaced by the account it signed in to.</summary>
    public async Task ReplaceAsync(Page page, bool animated = true)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (CurrentPage is not { } from || _stack.Count == 1)
        {
            SetRoot(page);
            return;
        }

        if (!await from.CanLeaveAsync() || !RaiseNavigating(NavigationKind.Replace, from, page)) return;

        AddPage(page);
        _stack[^1] = page;

        _history?.Replace(Location());

        await Switch(from, page, NavigationKind.Replace, isPop: false, animated);
    }

    private void PushHistory()
    {
        if (_history is null) return;

        _history.Push(Location());
        _historyDepth++;
        HistoryOwners.GetOrCreateValue(_history).Add(this);
    }

    private void BackHistory(int count)
    {
        int back = Math.Min(count, _historyDepth);
        if (_history is null || back <= 0) return;

        _history.Back(back);
        ForgetEntries(back);
    }

    /// <summary>The latest <paramref name="count"/> entries this stack pushed are gone, or
    /// no longer its own.</summary>
    private void ForgetEntries(int count)
    {
        count = Math.Min(count, _historyDepth);
        if (count <= 0) return;

        _historyDepth -= count;

        if (_history is null || !HistoryOwners.TryGetValue(_history, out List<NavigationHost>? owners)) return;

        for (int i = owners.Count - 1; i >= 0 && count > 0; i--)
        {
            if (!ReferenceEquals(owners[i], this)) continue;

            owners.RemoveAt(i);
            count--;
        }
    }

    /// <summary>The stack that pushed the entry the browser's back takes; null — none
    /// of the entries is a stack's.</summary>
    private NavigationHost? LatestEntryOwner() =>
        _history is not null && HistoryOwners.TryGetValue(_history, out List<NavigationHost>? owners) && owners.Count > 0
            ? owners[^1]
            : null;

    private bool RaiseNavigating(NavigationKind kind, Page? from, Page to)
    {
        if (Navigating is null) return true;

        var args = new NavigatingEventArgs(kind, from, to);
        Navigating(this, args);

        return !args.Cancel;
    }

    private void AddPage(Page page)
    {
        page.NavigationHost = this;
        page.IsVisible = false;

        // the new page lies over the others: it is drawn last
        Children.Add(page);
    }

    private void RemovePage(Page page)
    {
        Children.Remove(page);

        page.NavigationHost = null;
        page.CompleteResult();
    }

    // ===== routes =====

    /// <summary>Make pages for <paramref name="route"/> with <paramref name="factory"/>.
    /// A route is a name without slashes: <c>order</c>, <c>settings</c>.</summary>
    public void RegisterRoute(string route, Func<Page> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        RegisterRoute(route, _ => factory());
    }

    /// <summary>Make pages for <paramref name="route"/> with <paramref name="factory"/>,
    /// which gets the query of the location: <c>order?id=42</c> — <c>id</c> → <c>42</c>.</summary>
    public void RegisterRoute(string route, Func<IReadOnlyDictionary<string, string>, Page> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(factory);

        if (route.Contains('/') || route.Contains('?'))
            throw new ArgumentException("A route is one name: no slashes, no query.", nameof(route));

        _routes[route] = factory;
    }

    /// <summary>
    /// Go to a location made of routes: <c>order?id=42</c> pushes the page of the route
    /// <c>order</c>; <c>orders/order?id=42</c> pushes one after the other; a leading
    /// <c>/</c> goes back to the root first — the root's own route, if it has one, may
    /// start the path —; <c>..</c> goes back one page.
    /// </summary>
    /// <exception cref="ArgumentException">A route nobody registered.</exception>
    public async Task GoToAsync(string location, bool animated = true)
    {
        ArgumentNullException.ThrowIfNull(location);

        (string path, Dictionary<string, string> query) = ParseLocation(location);

        bool absolute = path.StartsWith('/');
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // checked before anything moves: half a path is worse than none
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];

            if (segment != ".." && !_routes.ContainsKey(segment) && !(absolute && i == 0 && segment == RootPage?.Route))
                throw new ArgumentException($"No page is registered for the route '{segment}'.", nameof(location));
        }

        if (absolute) await PopToRootAsync(animated: false);

        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];

            if (segment == "..")
            {
                await PopAsync(animated);
                continue;
            }

            if (absolute && i == 0 && segment == RootPage?.Route) continue;

            bool last = i == segments.Length - 1;
            IReadOnlyDictionary<string, string> parameters = last ? query : new Dictionary<string, string>(StringComparer.Ordinal);

            Page page = _routes[segment](parameters);
            page.Route ??= segment;
            page.Parameters = parameters;

            // only the last page is seen arriving
            await PushAsync(page, animated && last);
        }
    }

    private static (string Path, Dictionary<string, string> Query) ParseLocation(string location)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal);

        int mark = location.IndexOf('?');
        string path = mark < 0 ? location : location[..mark];

        if (mark >= 0)
        {
            foreach (string pair in location[(mark + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = pair.IndexOf('=');

                string key = Uri.UnescapeDataString((equals < 0 ? pair : pair[..equals]).Replace('+', ' '));
                string value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));

                query[key] = value;
            }
        }

        return (path.Trim(), query);
    }

    /// <summary>The stack as a location: the routes of its pages, the current page's query.
    /// What a browser shows after the <c>#</c>.</summary>
    public string Location()
    {
        var path = new System.Text.StringBuilder();

        foreach (Page page in _stack)
        {
            if (string.IsNullOrEmpty(page.Route)) continue;

            path.Append('/').Append(page.Route);
        }

        if (CurrentPage is { Parameters.Count: > 0 } current)
        {
            path.Append('?');
            path.Append(string.Join("&", current.Parameters.Select(p =>
                Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))));
        }

        return path.Length == 0 ? "/" : path.ToString();
    }

    // ===== the form: back requests and the browser's history =====

    protected override void OnAttached()
    {
        base.OnAttached();

        _form = FindOwner();

        if (_form is null) return;

        _form.AddBackHandler(this);

        // a form is mostly given its content before it is shown: the window, and
        // with it the browser's history, comes later
        _form.PlatformWindowAttached += OnPlatformWindowAttached;
        ClaimHistory();
    }

    protected override void OnDetached()
    {
        base.OnDetached();

        if (_form is not null)
        {
            _form.RemoveBackHandler(this);
            _form.PlatformWindowAttached -= OnPlatformWindowAttached;
        }

        // its entries stay in the browser's history, nobody's
        ForgetEntries(_historyDepth);

        _form = null;
        _history = null;
        _historyDepth = 0;
    }

    private void OnPlatformWindowAttached(object? sender, EventArgs e) => ClaimHistory();

    private void ClaimHistory()
    {
        if (!TracksBrowserHistory || _history is not null || _form?.PlatformWindow is not INavigationHistory history) return;

        _history = history;

        // a page opened at an address with a route: the stack is built by it, once.
        // Asked for only when there are routes to build it with: a stack without them
        // leaves the address to one that has some
        if (_routes.Count > 0 && history.TakeInitialRoute() is { Length: > 0 } route && route != "/")
        {
            // the entry the page was opened at becomes the root's: the pages of the route
            // are pushed after it, and going back through them ends at the root's address
            history.Replace(Location());

            OpenInitialRoute(route);
            return;
        }

        // the address names the root, so a reload comes back to it. Only a stack with
        // routes: the address of one without names nothing, and the second destination
        // of a scaffold would wipe the first one's
        if (_stack.Count > 0 && _routes.Count > 0) history.Replace(Location());
    }

    private async void OpenInitialRoute(string route)
    {
        // async void: started from OnAttached, which can't wait. A route that names no
        // page is an address typed by hand — the root page stays, as for a web page
        // whose fragment points nowhere
        try
        {
            await GoToAsync(route, animated: false);
        }
        catch (ArgumentException)
        {
        }
    }

    bool IBackHandler.ClosesOnEscape => false;

    bool IBackHandler.TryHandleBack(BackSource source)
    {
        if (!HandlesBackRequests || !CanGoBack) return false;

        // the browser's back took an entry of its history already: the stack that pushed it
        // goes back, even hidden — a destination of a scaffold left for another —, or the
        // stacks and the history stop matching, and a later back leaves the application
        if (source == BackSource.Browser && LatestEntryOwner() is { } owner)
        {
            if (!ReferenceEquals(owner, this)) return false;

            PopForBack(source, animated: IsEffectivelyVisible);
            return true;
        }

        if (!IsEffectivelyVisible) return false;

        PopForBack(source, animated: true);
        return true;
    }

    /// <summary>A pop the system asked for: nobody awaits it, and a page's CanLeaveAsync
    /// may still keep it. async void — the request is taken now, the pop ends later.</summary>
    private async void PopForBack(BackSource source, bool animated) => await PopCoreAsync(animated, source);

    // ===== the transition =====

    private Task Switch(
        Page from, Page to, NavigationKind kind, bool isPop, bool animated,
        PageTransition? forced = null, float startProgress = 0f)
    {
        FinishTransition();

        PageTransition transition = forced ?? (isPop ? Mirror(Transition) : Transition);

        bool canAnimate = animated
            && transition != PageTransition.None
            && TransitionDurationMs > 0
            && FindOwner()?.PlatformWindow is not null
            && !Motion.IsReduced;

        from.RaiseDisappearing();

        to.IsVisible = true;
        to.RaiseAppearing();

        var args = new NavigationEventArgs(kind, from, to);

        from.OnNavigatedFrom(args);
        to.OnNavigatedTo(args);

        // a pop: the page under the leaving one must be drawn under it — it was added
        // earlier, so it is, unless it was the incoming page of a replace
        if (!canAnimate)
        {
            Complete(from, to, kind, args);
            return Task.CompletedTask;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _incoming = to;
        _outgoing = from;
        _transitionIsPop = isPop;
        _activeTransition = transition;
        _progress = startProgress;
        _transitionDone = () =>
        {
            Complete(from, to, kind, args);
            done.TrySetResult();
        };

        // the slot is needed by the first frame: the layout is done now
        FindOwner()?.UpdateLayout();
        ApplyTransition();

        Animate(_progress, 1f);

        return done.Task;
    }

    private void Complete(Page from, Page to, NavigationKind kind, NavigationEventArgs args)
    {
        if (kind == NavigationKind.Push)
        {
            from.IsVisible = false;
        }
        else
        {
            RemovePage(from);
        }

        ResetPlacement(from);
        ResetPlacement(to);

        if (FocusOnNavigate) FindOwner()?.FocusFirstTextInput(to);

        Navigated?.Invoke(this, args);
        Invalidate();
    }

    private void Animate(float from, float to, Action? completed = null)
    {
        float remaining = Math.Abs(to - from);
        var duration = TimeSpan.FromMilliseconds(Math.Max(1, TransitionDurationMs * remaining));

        this.Animate("navigation", from, to, duration, Interpolators.Float,
            value =>
            {
                _progress = value;
                ApplyTransition();
                InvalidateVisual();
            },
            Easing.EaseOut,
            completed: completed ?? FinishTransition);
    }

    /// <summary>Bring the transition in progress to its end at once: a new one starts.</summary>
    private void FinishTransition()
    {
        Action? done = _transitionDone;

        _transitionDone = null;
        _incoming = null;
        _outgoing = null;
        _activeTransition = PageTransition.None;
        _progress = 1f;

        this.StopAnimation("navigation");

        done?.Invoke();
    }

    private void ResetPlacement(Page page)
    {
        page.Opacity = 1f;
        page.Position = _slot.Position;
    }

    private static PageTransition Mirror(PageTransition transition) => transition switch
    {
        PageTransition.SlideLeft => PageTransition.SlideRight,
        PageTransition.SlideRight => PageTransition.SlideLeft,
        PageTransition.SlideUp => PageTransition.SlideDown,
        PageTransition.SlideDown => PageTransition.SlideUp,
        _ => transition,
    };

    /// <summary>
    /// Place the two pages by the progress. A push slides the new page in over the old one,
    /// which drifts a third of the way back — the parallax of a phone's navigation —; a pop
    /// is the same backwards: the top page slides away and uncovers the one under it.
    /// </summary>
    private void ApplyTransition()
    {
        if (_incoming is not { } incoming || _outgoing is not { } outgoing) return;

        float p = _progress;
        float w = _slot.Width;
        float h = _slot.Height;
        float x = _slot.X;
        float y = _slot.Y;

        switch (_activeTransition)
        {
            case PageTransition.Fade:
                incoming.Opacity = p;
                outgoing.Opacity = 1f - p;
                break;

            // a push: the new page from the right, the old one drifts left
            case PageTransition.SlideLeft when !_transitionIsPop:
                incoming.Position = new Point(x + w * (1f - p), y);
                outgoing.Position = new Point(x - w * Parallax * p, y);
                break;

            // a pop of a push: the top page away to the right, the one under it back from the left
            case PageTransition.SlideRight when _transitionIsPop:
                outgoing.Position = new Point(x + w * p, y);
                incoming.Position = new Point(x - w * Parallax * (1f - p), y);
                break;

            // the rest move both pages whole, one after the other
            case PageTransition.SlideLeft:
                incoming.Position = new Point(x + w * (1f - p), y);
                outgoing.Position = new Point(x - w * p, y);
                break;

            case PageTransition.SlideRight:
                incoming.Position = new Point(x - w * (1f - p), y);
                outgoing.Position = new Point(x + w * p, y);
                break;

            // up: the new page rises over the old one, which stays — a page shown as a sheet
            case PageTransition.SlideUp when !_transitionIsPop:
                incoming.Position = new Point(x, y + h * (1f - p));
                break;

            case PageTransition.SlideDown when _transitionIsPop:
                outgoing.Position = new Point(x, y + h * p);
                break;

            case PageTransition.SlideUp:
                incoming.Position = new Point(x, y + h * (1f - p));
                outgoing.Position = new Point(x, y - h * p);
                break;

            case PageTransition.SlideDown:
                incoming.Position = new Point(x, y - h * (1f - p));
                outgoing.Position = new Point(x, y + h * p);
                break;
        }
    }

    /// <summary>How far the page under a pushed one drifts: a third of the width.</summary>
    private const float Parallax = 0.3f;

    // ===== the swipe back =====

    private bool CanBeginSwipeBack(PointerContact contact)
    {
        if (!SwipeBackEnabled || !CanGoBack || _transitionDone is not null) return false;

        float x = contact.DownLocation.X - GetAbsolutePosition().X;

        // the edge where a page came in from: the left one, the right one in a
        // right-to-left layout... the stack slides from the right whatever the direction
        return x >= 0 && x <= SwipeBackEdgeWidth;
    }

    private Page? _swipeFrom;

    private void OnSwipeStarted(object? sender, PanGestureEventArgs e)
    {
        if (!CanGoBack) return;

        FinishTransition();

        _dragging = true;
        _swipeFrom = _stack[^1];

        Page to = _stack[^2];

        // the page under shows while the top one is dragged; nothing is popped yet
        to.IsVisible = true;

        _incoming = to;
        _outgoing = _swipeFrom;
        _transitionIsPop = true;

        // whatever the stack's transition, a page dragged by the finger goes sideways
        _activeTransition = PageTransition.SlideRight;
        _progress = 0f;

        FindOwner()?.UpdateLayout();
        ApplyTransition();
    }

    private void OnSwipeUpdated(object? sender, PanGestureEventArgs e)
    {
        if (!_dragging || _slot.Width <= 0) return;

        _progress = Math.Clamp(e.TotalOffset.X / _slot.Width, 0f, 1f);
        ApplyTransition();
        InvalidateVisual();
    }

    private async void OnSwipeCompleted(object? sender, PanGestureEventArgs e)
    {
        if (!_dragging) return;

        // far enough, or flung: the page goes; otherwise it slides back
        bool go = _progress > 0.4f || e.Velocity.X > 600f;

        if (!go)
        {
            CancelSwipe();
            return;
        }

        // the drag hands its state to the pop, which goes on from where the finger left it
        Page? from = _swipeFrom;
        Page? under = _incoming;

        if (from is null || under is null || !ReferenceEquals(CurrentPage, from))
        {
            CancelSwipe();
            return;
        }

        if (!await from.CanLeaveAsync() || !RaiseNavigating(NavigationKind.Pop, from, under))
        {
            CancelSwipe();
            return;
        }

        float progress = _progress;

        // the drag's state goes to the pop, which starts where the finger left the page
        _incoming = null;
        _outgoing = null;
        _dragging = false;
        _swipeFrom = null;

        _stack.RemoveAt(_stack.Count - 1);
        BackHistory(1);

        await Switch(from, under, NavigationKind.Pop, isPop: true, animated: true, PageTransition.SlideRight, progress);
    }

    private void OnSwipeCancelled(object? sender, EventArgs e) => CancelSwipe();

    private void CancelSwipe()
    {
        if (!_dragging) return;

        Page? from = _swipeFrom;
        Page? under = _incoming;

        _dragging = false;
        _swipeFrom = null;

        if (from is null || under is null)
        {
            FinishTransition();
            return;
        }

        // back to where it was: the page under hides again
        _transitionDone = () =>
        {
            under.IsVisible = false;
            ResetPlacement(under);
            ResetPlacement(from);
            Invalidate();
        };

        Animate(_progress, 0f, FinishTransition);
    }

    // ===== layout =====

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        float width = 0, height = 0;

        // the visible pages only: the hidden ones mustn't size the stack
        foreach (UIElement child in Children)
        {
            if (!child.IsVisible) continue;

            child.Measure(inner);

            width = Math.Max(width, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return ResolveSize(new Size(width + Padding.Horizontal, height + Padding.Vertical), availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        _slot = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, contentSize.Width - Padding.Horizontal),
                Math.Max(0, contentSize.Height - Padding.Vertical)));

        foreach (UIElement child in Children)
        {
            if (child.IsVisible) child.Arrange(_slot);
        }

        // laid out in the middle of a transition: the pages go back where it had them
        if (_incoming is not null && _outgoing is not null)
            ApplyTransition();
    }
}

/// <summary>The browser's history, for a navigation stack that tracks it. The root window
/// of the browser's platform is one.</summary>
internal interface INavigationHistory
{
    /// <summary>The route the page was opened at — the address after the <c>#</c> —
    /// once: the first stack to ask builds itself by it, the others get null.</summary>
    string? TakeInitialRoute();

    /// <summary>A new entry, with the location in the address.</summary>
    void Push(string location);

    /// <summary>The current entry's location changes; no new entry.</summary>
    void Replace(string location);

    /// <summary>The stack went back by itself: the history goes back as many entries,
    /// and the platform doesn't take that for the user's back.</summary>
    void Back(int count);
}
