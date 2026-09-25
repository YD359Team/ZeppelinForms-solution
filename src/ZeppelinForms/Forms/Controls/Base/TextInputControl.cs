using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Base;

// Base/TextInputControl.cs
/// <summary>The basis of input fields: the blinking caret and its life cycle.
/// The editing logic belongs to derived classes.</summary>
/// <remarks>
/// The caret state is not stored but computed from time: the phase is the
/// remainder of dividing the time elapsed since the last reset by the period.
/// That is why the caret has no timer of its own, and the shared clock's
/// wake-up is needed only to redraw it at the moment it flips.
///
/// Previously the state was stored and toggled by a thread-pool timer.
/// A deferred tick that reached the UI queue after focus was lost turned
/// the caret back on, and it stayed on an unfocused field.
/// </remarks>
public abstract class TextInputControl : InteractiveControl
{
    private const double BlinkIntervalMs = 530;

    /// <summary>Time of the last blink reset by the form's clock.</summary>
    private TimeSpan _blinkStart;

    private IDisposable? _blinkWake;

    protected bool CaretVisible
    {
        get
        {
            if (!IsFocused) return false;

            // no clock — no frames to blink in either: show the caret
            // permanently, otherwise it would vanish for good
            if (FindOwner() is not { } owner) return true;

            double elapsed = (owner.Clock.Now - _blinkStart).TotalMilliseconds;

            return elapsed % (BlinkIntervalMs * 2) < BlinkIntervalMs;
        }
    }

    public override bool AcceptsTextInput => IsEnabled;

    protected TextInputControl()
    {
        Cursor = CursorKind.IBeam;
    }

    protected override void OnGotFocus()
    {
        RestartBlink();
    }

    protected override void OnLostFocus()
    {
        StopBlink();
    }

    /// <summary>The caret must be visible right after typing, moving or
    /// selecting — otherwise the cursor disappears exactly at the moment
    /// someone is looking at it.</summary>
    protected void ResetCaretBlink()
    {
        if (!IsFocused) return;

        RestartBlink();
        InvalidateVisual();
    }

    private void RestartBlink()
    {
        if (FindOwner() is not { } owner) return;

        _blinkStart = owner.Clock.Now;

        ScheduleBlink(owner);
    }

    private void ScheduleBlink(Form owner)
    {
        _blinkWake?.Dispose();

        double elapsed = (owner.Clock.Now - _blinkStart).TotalMilliseconds;
        double untilFlip = BlinkIntervalMs - elapsed % BlinkIntervalMs;

        _blinkWake = owner.Clock.Schedule(TimeSpan.FromMilliseconds(untilFlip), () =>
        {
            // focus may have left while the wake-up was waiting in the queue
            if (!IsFocused)
            {
                StopBlink();
                return;
            }

            InvalidateVisual();
            ScheduleBlink(owner);
        });
    }

    private void StopBlink()
    {
        _blinkWake?.Dispose();
        _blinkWake = null;
    }

    protected override void OnDetached()
    {
        // not Dispose: the control may be returned to the tree — on a page
        // switch, a panel rebuild, a drag. The wake-up is removed, and the phase
        // restores itself the next time focus is gained
        StopBlink();
    }
}