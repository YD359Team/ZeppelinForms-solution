using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

/// <param name="Scale">The ratio of the current distance between the fingers
/// to the distance at the start of the gesture.</param>
/// <param name="Center">The midpoint between the fingers — scaling goes around it.</param>
public sealed record class PinchGestureEventArgs(float Scale, Point Center);

/// <summary>Scaling with two fingers.</summary>
public sealed class PinchGestureRecognizer : GestureRecognizer
{
    private float _startDistance;
    private float _scale = 1f;

    /// <summary>Started was raised, and neither Completed nor Cancelled has closed
    /// the gesture yet. Completed goes out when the second finger lifts, while the
    /// recognizer stays Accepted until the first one lifts too — a cancel of that
    /// remaining contact used to deliver Cancelled on top of Completed, and
    /// a subscriber could roll back a scale it had already committed.</summary>
    private bool _started;

    protected override int MaxContacts => 2;

    public event EventHandler<PinchGestureEventArgs>? Started;
    public event EventHandler<PinchGestureEventArgs>? Updated;
    public event EventHandler<PinchGestureEventArgs>? Completed;
    public event EventHandler? Cancelled;

    protected override void OnBegin() => _started = false;

    protected override void OnContactAdded(PointerContact contact)
    {
        if (Contacts.Count < 2) return;

        // the count starts from the moment there are two fingers,
        // not from the first touch: before that there is no distance
        _startDistance = Distance();
        _scale = 1f;
    }

    protected override void OnContactRemoved(PointerContact contact)
    {
        if (Contacts.Count >= 2) return;

        _startDistance = 0;

        if (State == GestureState.Accepted && _started)
        {
            _started = false;
            Completed?.Invoke(this, MakeArgs());
        }
    }

    protected override void OnPointerMove(PointerEventArgs e)
    {
        if (Contacts.Count < 2 || _startDistance <= 0) return;

        float distance = Distance();

        if (State == GestureState.Accepted)
        {
            _scale = distance / _startDistance;
            Updated?.Invoke(this, MakeArgs());
            return;
        }

        // the same threshold as for dragging: a change in the distance between
        // the fingers is the same physical movement, and there is no reason
        // to measure it with another yardstick
        if (MathF.Abs(distance - _startDistance) < PointerThresholds.DragSlop(Contacts[0].Kind, Display))
            return;

        Accept();

        _scale = distance / _startDistance;
        _started = true;
        Started?.Invoke(this, MakeArgs());
    }

    protected override void OnCancel()
    {
        // lost or refused before it started — there is nothing to cancel
        if (!_started) return;

        _started = false;
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private float Distance()
    {
        Point a = Contacts[0].Location;
        Point b = Contacts[1].Location;

        float dx = b.X - a.X;
        float dy = b.Y - a.Y;

        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private PinchGestureEventArgs MakeArgs()
    {
        // after a finger leaves, one remains — it is taken as the center,
        // otherwise Completed would come with a reference to a contact that no longer exists
        Point center = Contacts.Count >= 2
            ? new Point(
                (Contacts[0].Location.X + Contacts[1].Location.X) / 2f,
                (Contacts[0].Location.Y + Contacts[1].Location.Y) / 2f)
            : Contacts.Count == 1 ? Contacts[0].Location : Point.Empty;

        return new PinchGestureEventArgs(_scale, center);
    }
}