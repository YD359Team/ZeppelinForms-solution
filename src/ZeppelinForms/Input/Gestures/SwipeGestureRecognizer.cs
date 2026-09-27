using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

public enum SwipeDirection
{
    Left,
    Right,
    Up,
    Down,
}

/// <param name="Velocity">The gesture's average velocity, logical units per second.</param>
public sealed record class SwipeGestureEventArgs(
    SwipeDirection Direction, float Velocity, Point Location);

/// <summary>A quick movement in one direction.</summary>
public sealed class SwipeGestureRecognizer : GestureRecognizer
{
    /// <summary>Below this velocity the movement counts as a drag rather than
    /// a fling. Logical units per second.</summary>
    public float MinimumVelocity { get; set; } = 300f;

    /// <summary>Which directions to accept. Empty — any.</summary>
    public SwipeDirection[]? AllowedDirections { get; set; }

    public event EventHandler<SwipeGestureEventArgs>? Swiped;

    /// <summary>The decision is made on the release: before it a swipe can't be
    /// told apart from the start of a drag, and an early victory would take the
    /// contact away from a pan that has just as much right to it.</summary>
    protected override void OnPointerUp(PointerEventArgs e)
    {
        if (Contact is not PointerContact contact) return;

        float dx = contact.Location.X - contact.DownLocation.X;
        float dy = contact.Location.Y - contact.DownLocation.Y;

        float distance = MathF.Sqrt(dx * dx + dy * dy);

        if (distance < PointerThresholds.SwipeMinDistance(Display))
        {
            Reject();
            return;
        }

        // division by zero: a contact shorter than a millisecond
        // is quite within the system's power to deliver
        float velocity = distance / Math.Max(1, contact.Duration) * 1000f;

        if (velocity < MinimumVelocity)
        {
            Reject();
            return;
        }

        SwipeDirection direction = MathF.Abs(dx) >= MathF.Abs(dy)
            ? dx >= 0 ? SwipeDirection.Right : SwipeDirection.Left
            : dy >= 0 ? SwipeDirection.Down : SwipeDirection.Up;

        if (AllowedDirections is { Length: > 0 } allowed && Array.IndexOf(allowed, direction) < 0)
        {
            Reject();
            return;
        }

        Accept();

        Swiped?.Invoke(this, new SwipeGestureEventArgs(direction, velocity, contact.Location));
    }

    protected override void OnCancel() { }
}