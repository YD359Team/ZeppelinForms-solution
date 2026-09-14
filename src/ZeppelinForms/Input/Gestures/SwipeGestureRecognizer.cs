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

/// <param name="Velocity">Средняя скорость жеста, логических единиц в секунду.</param>
public sealed record class SwipeGestureEventArgs(
    SwipeDirection Direction, float Velocity, Point Location);

/// <summary>Быстрое проведение в одну сторону.</summary>
public sealed class SwipeGestureRecognizer : GestureRecognizer
{
    /// <summary>Ниже этой скорости движение считается перетаскиванием,
    /// а не броском. Логических единиц в секунду.</summary>
    public float MinimumVelocity { get; set; } = 300f;

    /// <summary>Какие направления принимать. Пусто — любые.</summary>
    public SwipeDirection[]? AllowedDirections { get; set; }

    public event EventHandler<SwipeGestureEventArgs>? Swiped;

    /// <summary>Решение принимается на отпускании: до него свайп
    /// неотличим от начала перетаскивания, и ранняя победа отобрала бы
    /// контакт у pan, который имеет на него столько же прав.</summary>
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

        // деление на ноль: контакт длительностью меньше миллисекунды
        // системе вполне по силам отдать
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