using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

public sealed record class LongPressGestureEventArgs(Point Location);

/// <summary>Holding without movement.</summary>
/// <remarks>
/// The only gesture that needs an external wake-up: while the finger is still,
/// no events come at all, and there is nothing in the incoming stream
/// to measure half a second by.
/// </remarks>
public sealed class LongPressGestureRecognizer : GestureRecognizer
{
    private IDisposable? _timer;

    public int DelayMs { get; set; } = PointerThresholds.LongPressMs;

    public event EventHandler<LongPressGestureEventArgs>? Triggered;
    public event EventHandler? Cancelled;

    protected override void OnBegin() =>
        _timer = Element?.FindOwner()?.Schedule(DelayMs, Fire);

    protected override void OnPointerMove(PointerEventArgs e)
    {
        if (Contact is not PointerContact contact) return;

        // the same tolerance as a tap: a hold is a press that simply lasted
        // longer, and it may tremble exactly as much
        if (contact.TravelDistance > PointerThresholds.TapSlop(contact.Kind, Display))
            Reject();
    }

    /// <summary>Released before the time is up — that's an ordinary press, not ours.</summary>
    protected override void OnPointerUp(PointerEventArgs e) => Reject();

    protected override void OnCancel()
    {
        Stop();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnContactRemoved(PointerContact contact) => Stop();

    private void Fire()
    {
        // the wake-up may fire after the finger has already left: Dispose doesn't
        // catch up with a call already waiting in the UI queue, so the state check
        // decides, not the cancellation of the timer
        if (State != GestureState.Possible || Contact is not PointerContact contact) return;

        Accept();

        Triggered?.Invoke(this, new LongPressGestureEventArgs(contact.Location));
    }

    private void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }
}