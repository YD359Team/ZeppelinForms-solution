using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

public enum PanDirection
{
    Both,
    Horizontal,
    Vertical,
}

public sealed record class PanGestureEventArgs(
    Point Location, Point Delta, Point TotalOffset, Point DownLocation)
{
    /// <summary>The pointer velocity in pixels per second. On the release —
    /// the fling velocity over the last hundred milliseconds.</summary>
    public Point Velocity { get; init; }
}

/// <summary>Dragging after the break threshold has been passed.</summary>
public sealed class PanGestureRecognizer : GestureRecognizer
{
    private readonly VelocityTracker _velocity = new();
    private Point _last;

    /// <summary>Started was raised for the current contact, and neither Completed
    /// nor Cancelled has closed it yet. Cancelled goes out only while this is set:
    /// OnCancel also comes for a pan that refused the contact before the threshold
    /// or lost it to another gesture, and a subscriber must not get the cancel
    /// of a gesture it never saw start.</summary>
    private bool _started;

    /// <summary>Which direction counts as ours. A directed pan refuses the contact
    /// if the movement goes mostly across — otherwise a vertical list would take
    /// horizontal gestures away from its neighbour.</summary>
    public PanDirection Direction { get; set; } = PanDirection.Both;

    /// <summary>Asked at the very start of the contact: whether to take it at all.
    /// A scrolling panel refuses if there is nothing to scroll — otherwise it would
    /// take the gesture away from an outer panel that has somewhere to go.</summary>
    public Func<PointerContact, bool>? CanBegin { get; set; }

    public event EventHandler<PanGestureEventArgs>? Started;
    public event EventHandler<PanGestureEventArgs>? Updated;
    public event EventHandler<PanGestureEventArgs>? Completed;
    public event EventHandler? Cancelled;

    protected override void OnBegin()
    {
        _started = false;
        _last = Contact?.DownLocation ?? Point.Empty;
        _velocity.Reset();

        if (Contact is { } contact)
        {
            if (CanBegin is { } canBegin && !canBegin(contact))
            {
                Reject();
                return;
            }

            _velocity.Add(contact.DownLocation, contact.DownTimestamp);
        }
    }

    protected override void OnPointerMove(PointerEventArgs e)
    {
        if (Contact is not PointerContact contact) return;

        _velocity.Add(contact.Location, e.Timestamp);

        if (State == GestureState.Accepted)
        {
            Updated?.Invoke(this, MakeArgs(contact, e.Timestamp));
            _last = contact.Location;
            return;
        }

        float dx = contact.Location.X - contact.DownLocation.X;
        float dy = contact.Location.Y - contact.DownLocation.Y;

        float travel = Direction switch
        {
            PanDirection.Horizontal => MathF.Abs(dx),
            PanDirection.Vertical => MathF.Abs(dy),
            _ => MathF.Abs(dx) + MathF.Abs(dy),
        };

        if (travel < PointerThresholds.DragSlop(contact.Kind, Display)) return;

        // it went further across than along — the gesture isn't ours, and holding
        // the contact any longer means hindering whoever waits for the perpendicular one
        if (Direction == PanDirection.Horizontal && MathF.Abs(dy) > MathF.Abs(dx))
        {
            Reject();
            return;
        }

        if (Direction == PanDirection.Vertical && MathF.Abs(dx) > MathF.Abs(dy))
        {
            Reject();
            return;
        }

        Accept();

        // from this moment the gesture is ours, and a release outside
        // the window must reach us
        CaptureContact();

        // the start is counted from the press point, not from the current one:
        // the threshold is a recognition delay, not a lost distance
        _last = contact.DownLocation;
        _started = true;
        Started?.Invoke(this, MakeArgs(contact, e.Timestamp));
        _last = contact.Location;
    }

    protected override void OnPointerUp(PointerEventArgs e)
    {
        if (State != GestureState.Accepted || Contact is not PointerContact contact) return;

        _velocity.Add(contact.Location, e.Timestamp);

        // the gesture is closed: a cancel of the contact after this
        // must not reach the subscriber as a second ending
        _started = false;
        Completed?.Invoke(this, MakeArgs(contact, e.Timestamp));
    }

    protected override void OnCancel()
    {
        if (!_started) return;

        _started = false;
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private PanGestureEventArgs MakeArgs(PointerContact contact, long timestamp) => new(
        contact.Location,
        new Point(contact.Location.X - _last.X, contact.Location.Y - _last.Y),
        new Point(
            contact.Location.X - contact.DownLocation.X,
            contact.Location.Y - contact.DownLocation.Y),
        contact.DownLocation)
    {
        Velocity = _velocity.GetVelocity(timestamp),
    };
}