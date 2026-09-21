using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Input.Pointer;

/// <summary>
/// Скорость указателя по последним отсчётам — для инерции после броска.
/// </summary>
/// <remarks>
/// Берётся не средняя скорость всего жеста, а скорость последних
/// ста миллисекунд: палец мог долго тянуть медленно и в конце резко
/// бросить, и прокрутка должна ответить на бросок, а не на всё движение.
///
/// Окно, а не два последних отсчёта: метки времени у событий грубые
/// (на Windows — шаг около 16 мс), и скорость по двум соседним точкам
/// прыгает вдвое от события к событию.
/// </remarks>
public sealed class VelocityTracker
{
    private const int Capacity = 20;
    private const long WindowMs = 100;

    /// <summary>Палец замер дольше этого перед отпусканием — броска не было.</summary>
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

    /// <summary>Скорость в пикселях в секунду на момент releaseMs.</summary>
    public Point GetVelocity(long releaseMs)
    {
        if (_count < 2) return Point.Empty;

        var newest = _samples[(_head - 1 + Capacity) % Capacity];

        if (releaseMs - newest.Timestamp > StillMs) return Point.Empty;

        // самый старый отсчёт, ещё попадающий в окно
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