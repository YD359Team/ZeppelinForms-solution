using System.Diagnostics;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Animation;

/// <summary>
/// The form's only clock: frame time, animations and deferred work.
/// </summary>
/// <remarks>
/// Three reasons why this is one object rather than three.
///
/// Time is monotonic and high-resolution. Environment.TickCount64 on
/// Windows ticks every 15.6 ms, and with a 16 ms frame the animation step
/// jumped between zero and thirty milliseconds — visible to the eye
/// even on a toggle switch.
///
/// Frames run only while there is something to move. An animation in
/// a hidden subtree does not advance — and no longer keeps frames coming:
/// a collapsed Loader woke the window sixty times a second for nothing.
/// The animation itself is not removed, though: the page will come back,
/// and it must come alive again.
///
/// Deferred work shares one timer. Previously the caret, the tooltip,
/// the toast and the long press each started their own System.Threading.Timer,
/// and each marshalled itself to the UI thread on its own. On top of that
/// the caret stored its state instead of computing it from time, and
/// a deferred tick that reached the queue after focus was lost
/// turned it back on.
/// </remarks>
internal sealed class FrameClock(Form form) : IDisposable
{
    /// <summary>Cap on the animation step. A frame may not have arrived for
    /// half a second — the window was being dragged by its corner, the
    /// application went to the background. Without the cap the very first
    /// frame after the pause brings every animation to its end, and
    /// a transition the viewer never saw simply collapses.</summary>
    private const double MaxFrameDeltaMs = 100;

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    private readonly List<IAnimation> _animations = [];
    private readonly List<Wake> _wakes = [];
    private readonly List<Wake> _due = [];

    private System.Threading.Timer? _timer;
    private TimeSpan _lastFrame;
    private bool _suspended;

    /// <summary>Monotonic time since the form was created.</summary>
    public TimeSpan Now => _stopwatch.Elapsed;

    /// <summary>Whether there is an animation that can be seen. An invisible
    /// one is not advanced, which means no frames are needed for it.</summary>
    private bool HasVisibleAnimation
    {
        get
        {
            foreach (IAnimation animation in _animations)
                if (animation.Target is not UIElement element || element.IsEffectivelyVisible)
                    return true;

            return false;
        }
    }

    // ===== animations =====

    public void Add(IAnimation animation)
    {
        // one animation per "object + property" pair.
        // The displaced one is removed with its completed invoked, otherwise
        // the state it was supposed to tidy up stays in the middle —
        // this is exactly why PageControl left pages hanging
        for (int i = _animations.Count - 1; i >= 0; i--)
        {
            IAnimation existing = _animations[i];

            if (!ReferenceEquals(existing.Target, animation.Target) ||
                existing.Key != animation.Key)
                continue;

            _animations.RemoveAt(i);

            // without bringing the value to the end: the new animation starts
            // from its own from, and a jump to the end would cause a flicker
            existing.Cancel(applyFinalValue: false);
        }

        _animations.Add(animation);

        Review();
    }

    public void Remove(object target, string key)
    {
        for (int i = _animations.Count - 1; i >= 0; i--)
        {
            IAnimation existing = _animations[i];

            if (!ReferenceEquals(existing.Target, target) || existing.Key != key)
                continue;

            _animations.RemoveAt(i);
            existing.Cancel(applyFinalValue: false);
        }

        Review();
    }

    /// <summary>Remove animations whose targets are leaving the tree.</summary>
    public void CancelIn(UIElement root)
    {
        for (int i = _animations.Count - 1; i >= 0; i--)
        {
            if (_animations[i].Target is not UIElement element || !IsInTree(root, element))
                continue;

            IAnimation animation = _animations[i];
            _animations.RemoveAt(i);

            // the target is leaving the tree: there is no need to bring
            // the value to the end — nothing will draw it anymore.
            // Completion is still up to the implementation: Animation<T>
            // calls completed anyway, because it tidies up state, not the picture
            animation.Cancel(applyFinalValue: false);
        }

        Review();
    }

    private static bool IsInTree(UIElement root, UIElement candidate)
    {
        for (UIElement? current = candidate; current is not null; current = current.Parent)
            if (ReferenceEquals(current, root))
                return true;

        return false;
    }

    /// <summary>A frame from the platform.</summary>
    public void Tick()
    {
        TimeSpan now = _stopwatch.Elapsed;
        double deltaMs = (now - _lastFrame).TotalMilliseconds;
        _lastFrame = now;

        Advance(TimeSpan.FromMilliseconds(Math.Clamp(deltaMs, 0, MaxFrameDeltaMs)));
    }

    /// <summary>Advance animations by the given time. Separate from Tick
    /// for the sake of tests: there time must move on command,
    /// not by the stopwatch.</summary>
    internal void Advance(TimeSpan elapsed)
    {
        bool wholeWindow = false;

        // over a snapshot rather than the live list: Advance calls completed
        // right inside itself, and that may both remove animations and add
        // new ones — a page transition does exactly this. With that going on,
        // the indices drift out from under you
        IAnimation[] running = [.. _animations];

        foreach (IAnimation animation in running)
        {
            // may have been removed by a neighbouring animation's completed
            if (!_animations.Contains(animation)) continue;

            // an animation in a hidden subtree is neither advanced nor redrawn.
            // We don't remove it: the page will come back, and the animation
            // must come alive again. PageControl hides pages without detaching
            // them, so Detached cannot be relied on here
            if (animation.Target is UIElement hidden && !hidden.IsEffectivelyVisible)
                continue;

            bool alive = animation.Advance(elapsed);

            // redraw the target whether or not the animation survives
            // to the next frame: its last frame must be shown too
            switch (animation.Target)
            {
                // an animation of the form itself — the theme-change ripple,
                // for example — goes beyond the bounds of any single element
                case Form: wholeWindow = true; break;
                case UIElement element: element.InvalidateVisual(); break;
            }

            if (!alive) _animations.Remove(animation);
        }

        Review();

        if (wholeWindow) form.InvalidateVisual();
    }

    // ===== frame delivery =====

    /// <summary>Reconsider whether frames are needed right now. Called after
    /// any change in the set of animations and from Form.Invalidate:
    /// visibility is a layout property, and a change of it always
    /// passes through there.</summary>
    public void Review()
    {
        if (form.PlatformWindow?.Frames is not { } frames) return;

        if (_suspended || !HasVisibleAnimation)
        {
            if (frames.IsRunning) frames.Stop();
            return;
        }

        if (frames.IsRunning) return;

        // an unknown amount of time has passed between stop and start:
        // the first step is counted from this moment, not from a long-gone frame
        _lastFrame = _stopwatch.Elapsed;
        frames.Start(form.FrameIntervalMs);
    }

    /// <summary>The application went to the background or the window was minimized.</summary>
    public void Suspend()
    {
        _suspended = true;
        form.PlatformWindow?.Frames.Stop();
    }

    public void Resume()
    {
        _suspended = false;
        Review();
    }

    /// <summary>The window is gone: the frame timer lived in it,
    /// and deferred work has nowhere to return to.</summary>
    public void Stop()
    {
        form.PlatformWindow?.Frames.Stop();
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    // ===== deferred work =====

    /// <summary>Run an action on the UI thread after a delay.</summary>
    /// <returns>Cancellation: dispose the result to prevent the call.</returns>
    /// <remarks>
    /// Cancellation and firing are a race: the wake-up may already have gone
    /// into the UI queue by the time it is cancelled. So the invoked code
    /// must check for itself whether it is still relevant, rather than
    /// rely on cancellation.
    /// </remarks>
    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var wake = new Wake(_stopwatch.Elapsed + delay, action);

        _wakes.Add(wake);
        Rearm();

        return wake;
    }

    private void Rearm()
    {
        TimeSpan? earliest = null;

        for (int i = _wakes.Count - 1; i >= 0; i--)
        {
            Wake wake = _wakes[i];

            if (wake.Action is null)
            {
                _wakes.RemoveAt(i);
                continue;
            }

            if (earliest is null || wake.Due < earliest) earliest = wake.Due;
        }

        if (earliest is null)
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        _timer ??= new System.Threading.Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);

        double delayMs = Math.Max(0, (earliest.Value - _stopwatch.Elapsed).TotalMilliseconds);

        _timer.Change((long)delayMs, Timeout.Infinite);
    }

    // ticks on a pool thread — marshal it, everything after this is on the UI thread
    private void OnTimer(object? state) => form.Invoke(RunDue);

    private void RunDue()
    {
        TimeSpan now = _stopwatch.Elapsed;

        // first take it off the list, then invoke: a job may schedule
        // a new deferred job — this is exactly how the caret re-arms itself
        for (int i = _wakes.Count - 1; i >= 0; i--)
        {
            Wake wake = _wakes[i];

            if (wake.Action is not null && wake.Due > now) continue;

            _wakes.RemoveAt(i);

            if (wake.Action is not null) _due.Add(wake);
        }

        try
        {
            foreach (Wake wake in _due)
                wake.Action?.Invoke();
        }
        finally
        {
            _due.Clear();
            Rearm();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;

        _wakes.Clear();
        _animations.Clear();
    }

    private sealed class Wake(TimeSpan due, Action action) : IDisposable
    {
        public TimeSpan Due { get; } = due;

        /// <summary>null — the job was cancelled. The nearest Rearm takes it off
        /// the list: removing it from the middle right now would mean fighting
        /// with RunDue, which may be running at this very moment.</summary>
        public Action? Action { get; private set; } = action;

        public void Dispose() => Action = null;
    }
}