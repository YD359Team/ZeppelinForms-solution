using System;
using System.Collections.Generic;
using System.Text;

namespace ZeppelinForms.Browser;

/// <summary>
/// Frames come from requestAnimationFrame. The interval is kept by skipping frames
/// rather than by a timer: the browser won't wake us more often than the display
/// rate anyway, and a setInterval of our own next to rAF would give jerky animation.
/// </summary>
internal sealed class BrowserFrameDriver(Action scheduleFrame, Action repaint) : IFrameDriver
{
    private int _intervalMs;
    private double _lastFrameMs;

    public bool IsRunning { get; private set; }

    public void Start(int intervalMs)
    {
        if (IsRunning) return;

        _intervalMs = intervalMs;

        // 0 means "there has been no tick yet": the first frame after Start is given
        // out right away, otherwise the animation would start with a one-interval delay
        _lastFrameMs = 0;
        IsRunning = true;

        scheduleFrame();
    }

    public void Stop() => IsRunning = false;

    /// <summary>A single repaint without animation. As on X11, this is exactly
    /// a repaint, not a tick: there are no animations to recompute here.</summary>
    public void RequestFrame() => repaint();

    /// <summary>Whether it is time to give out a tick. Called from the rAF handler,
    /// and orders the next frame itself while continuous delivery is running.</summary>
    internal bool ShouldTick(double timestampMs)
    {
        if (!IsRunning) return false;

        scheduleFrame();

        // the interval is a ceiling on the rate, not a strict measure: vsync comes
        // with jitter, and a tight comparison dropped every second frame, turning
        // sixty frames into thirty. A quarter of the interval covers the jitter,
        // while an extra frame on a 120 Hz display is still skipped
        if (_lastFrameMs != 0 && timestampMs - _lastFrameMs < _intervalMs * 0.75)
            return false;

        _lastFrameMs = timestampMs;
        return true;
    }
}