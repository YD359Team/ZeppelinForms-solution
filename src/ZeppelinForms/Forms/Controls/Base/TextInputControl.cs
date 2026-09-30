using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

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
public abstract partial class TextInputControl : InteractiveControl
{
    private const double BlinkIntervalMs = 530;

    // ===== underline =====
    //
    // Fluent marks a field by its lower edge rather than by the whole border:
    // a slightly darker line at rest and a thick accent one under focus. It lives
    // here rather than in TextBox so that every field — MaskedTextBox and
    // NumericUpDown too — looks the same in one theme.

    /// <summary>The lower edge at rest. Transparent — none, the border as it is.</summary>
    [Styled(Category = "Underline")]
    public partial Color UnderlineColor { get; set; }
    private static Color UnderlineColorDefault => Colors.Transparent;

    /// <summary>The lower edge while the field has focus. Transparent — none.</summary>
    /// <remarks>Shown on any focus, not only a visible one: like the caret,
    /// it marks where typing goes, and a click is as good a way there as Tab.</remarks>
    [Styled(Category = "Underline")]
    public partial Color FocusUnderlineColor { get; set; }
    private static Color FocusUnderlineColorDefault => Colors.Transparent;

    [Styled(Category = "Underline")]
    public partial float FocusUnderlineThickness { get; set; }
    private static float FocusUnderlineThicknessDefault => 2f;

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

    /// <summary>Whether the focused underline is shown now.</summary>
    private bool UnderlineFocused => IsFocused && FocusUnderlineColor.A > 0;

    /// <summary>The underline for the current state. Derived fields put
    /// their own states above it — a validation error, for one.</summary>
    protected virtual Color CurrentUnderlineColor =>
        UnderlineFocused ? FocusUnderlineColor : UnderlineColor;

    /// <summary>The underline over the border. It is the border's own lower edge
    /// filled again, clipped to a band: that way it follows the rounded corners
    /// instead of sticking out of them as a straight bar would.</summary>
    protected override void DrawDecoration(Graphics g)
    {
        Color color = CurrentUnderlineColor;

        if (color.A == 0) return;

        // at rest the line is as thin as the border it replaces
        float thickness = UnderlineFocused
            ? FocusUnderlineThickness
            : Math.Max(BorderWidth, 1f);

        if (thickness <= 0f) return;

        Rectangle bounds = LocalBounds;

        g.Save();
        g.ClipRect(new Rectangle(
            new Point(bounds.X, bounds.Bottom - thickness),
            new Size(bounds.Width, thickness)));

        g.FillRoundRectangle(bounds, CornerRadius, color);

        g.Restore();
    }

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