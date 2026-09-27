using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

/// <summary>A short touch without movement.</summary>
/// <remarks>
/// For the primary contact this is almost a duplicate of the regular Click —
/// that one comes earlier and works as it always did. The point of the recognizer
/// is in the rest: it sees non-primary contacts too, and it takes part in
/// arbitration, that is, it can lose to a pan, which Click can't.
/// </remarks>
public sealed class TapGestureRecognizer : GestureRecognizer
{
    public event EventHandler? Tapped;

    protected override void OnPointerMove(PointerEventArgs e)
    {
        if (Contact is not PointerContact contact) return;

        if (contact.TravelDistance > PointerThresholds.TapSlop(contact.Kind, Display))
            Reject();
    }

    protected override void OnPointerUp(PointerEventArgs e)
    {
        if (Contact is not PointerContact contact) return;

        if (contact.Duration > PointerThresholds.TapMaxDurationMs)
        {
            Reject();
            return;
        }

        // accepted on the release: before it a tap can't be told apart from the
        // start of a drag, and an early victory would take the contact away from
        // a pan that hasn't reached its threshold yet
        Accept();
        Tapped?.Invoke(this, EventArgs.Empty);
    }
}