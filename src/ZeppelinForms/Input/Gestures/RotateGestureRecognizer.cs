using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

/// <param name="Angle">The rotation since the start of the gesture, in degrees. Clockwise is plus.</param>
public sealed record class RotateGestureEventArgs(float Angle, Point Center);

/// <summary>Rotation with two fingers.</summary>
public sealed class RotateGestureRecognizer : GestureRecognizer
{
    /// <summary>The threshold in degrees rather than millimeters: an angle has no
    /// physical length, and screen density doesn't affect it. The distance between
    /// the fingers does, though — with the fingers close together the angle jumps,
    /// so there is a check for a minimum base below.</summary>
    public float ThresholdDegrees { get; set; } = 8f;

    /// <summary>The minimum distance between the fingers at which the angle
    /// makes sense at all. In millimeters.</summary>
    public float MinimumSpanMm { get; set; } = 12f;

    private float _startAngle;
    private float _angle;

    /// <summary>Started was raised, and neither Completed nor Cancelled has closed
    /// the gesture yet — see the same field in PinchGestureRecognizer.</summary>
    private bool _started;

    protected override int MaxContacts => 2;

    public event EventHandler<RotateGestureEventArgs>? Started;
    public event EventHandler<RotateGestureEventArgs>? Updated;
    public event EventHandler<RotateGestureEventArgs>? Completed;
    public event EventHandler? Cancelled;

    protected override void OnBegin() => _started = false;

    protected override void OnContactAdded(PointerContact contact)
    {
        if (Contacts.Count < 2) return;

        _startAngle = Angle();
        _angle = 0;
    }

    protected override void OnContactRemoved(PointerContact contact)
    {
        if (Contacts.Count >= 2) return;

        if (State == GestureState.Accepted && _started)
        {
            _started = false;
            Completed?.Invoke(this, MakeArgs());
        }
    }

    protected override void OnPointerMove(PointerEventArgs e)
    {
        if (Contacts.Count < 2) return;

        if (Span() < Display.MillimetersToLogical(MinimumSpanMm)) return;

        float delta = Normalize(Angle() - _startAngle);

        if (State == GestureState.Accepted)
        {
            _angle = delta;
            Updated?.Invoke(this, MakeArgs());
            return;
        }

        if (MathF.Abs(delta) < ThresholdDegrees) return;

        Accept();

        _angle = delta;
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

    private float Angle()
    {
        Point a = Contacts[0].Location;
        Point b = Contacts[1].Location;

        return MathF.Atan2(b.Y - a.Y, b.X - a.X) * (180f / MathF.PI);
    }

    private float Span()
    {
        Point a = Contacts[0].Location;
        Point b = Contacts[1].Location;

        float dx = b.X - a.X;
        float dy = b.Y - a.Y;

        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Bring the difference into the range from -180 to 180: without this,
    /// crossing 180 degrees would give a jump by a full revolution.</summary>
    private static float Normalize(float degrees)
    {
        while (degrees > 180f) degrees -= 360f;
        while (degrees < -180f) degrees += 360f;

        return degrees;
    }

    private RotateGestureEventArgs MakeArgs()
    {
        Point center = Contacts.Count >= 2
            ? new Point(
                (Contacts[0].Location.X + Contacts[1].Location.X) / 2f,
                (Contacts[0].Location.Y + Contacts[1].Location.Y) / 2f)
            : Contacts.Count == 1 ? Contacts[0].Location : Point.Empty;

        return new RotateGestureEventArgs(_angle, center);
    }
}