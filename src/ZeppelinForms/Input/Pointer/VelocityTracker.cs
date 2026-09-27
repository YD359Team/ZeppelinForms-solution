using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Input.Pointer;

/// <summary>
/// The pointer velocity from the latest samples — for inertia after a fling.
/// </summary>
/// <remarks>
/// Not the average velocity of the whole gesture but the velocity of the last
/// hundred milliseconds: a finger may have dragged slowly for a long time and
/// then flung sharply at the end, and scrolling must respond to the fling,
/// not to the whole movement.
///
/// A window rather than the two latest samples: event timestamps are coarse
/// (about 16 ms steps on Windows), and a velocity from two neighbouring points
/// jumps twofold from event to event.
/// </remarks>
public sealed class VelocityTracker
{
    private const int Capacity = 20;
    private const long WindowMs = 100;

    /// <summary>The finger was still longer than this before the release — there was no fling.</summary>
    private const long StillMs = 40;

    private readonly (Point Location, long Timestamp)[] _samples = new (Point, long)[Capacity];
    private int _count;
    private int _head;

    public void Reset()
    {
        _count = 0;
        _head = 0;
    }

    public void Add(Point location, long timestampMs)
    {
        _samples[_head] = (location, timestampMs);
        _head = (_head + 1) % Capacity;
        _count = Math.Min(_count + 1, Capacity);
    }

    /// <summary>The velocity in pixels per second at the moment releaseMs.</summary>
    public Point GetVelocity(long releaseMs)
    {
        if (_count < 2) return Point.Empty;

        var newest = _samples[(_head - 1 + Capacity) % Capacity];

        if (releaseMs - newest.Timestamp > StillMs) return Point.Empty;

        // the oldest sample still inside the window
        var oldest = newest;

        for (int i = 2; i <= _count; i++)
        {
            var sample = _samples[(_head - i + Capacity) % Capacity];

            if (newest.Timestamp - sample.Timestamp > WindowMs) break;

            oldest = sample;
        }

        long dt = newest.Timestamp - oldest.Timestamp;

        if (dt <= 0) return Point.Empty;

        return new Point(
            (newest.Location.X - oldest.Location.X) * 1000f / dt,
            (newest.Location.Y - oldest.Location.Y) * 1000f / dt);
    }
}