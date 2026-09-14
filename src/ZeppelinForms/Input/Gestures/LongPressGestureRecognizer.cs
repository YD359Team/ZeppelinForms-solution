using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

public sealed record class LongPressGestureEventArgs(Point Location);

/// <summary>Удержание без движения.</summary>
/// <remarks>
/// Единственный жест, которому нужен внешний будильник: пока палец
/// неподвижен, событий не приходит вовсе, и отмерить полсекунды
/// по входящему потоку нечем.
/// </remarks>
public sealed class LongPressGestureRecognizer : GestureRecognizer
{
    private IDisposable? _timer;

    public int DelayMs { get; set; } = PointerThresholds.LongPressMs;

    public event EventHandler<LongPressGestureEventArgs>? Triggered;
    public event EventHandler? Cancelled;

    protected override void OnBegin() =>
        _timer = Element?.FindOwner()?.Schedule(DelayMs, Fire);

    protected override void OnPointerMove(PointerEventArgs e)
    {
        if (Contact is not PointerContact contact) return;

        // допуск тот же, что у касания: удержание — это нажатие, которое
        // просто затянулось, и дрожать ему позволено ровно столько же
        if (contact.TravelDistance > PointerThresholds.TapSlop(contact.Kind, Display))
            Reject();
    }

    /// <summary>Отпустили раньше срока — это обычное нажатие, не наше.</summary>
    protected override void OnPointerUp(PointerEventArgs e) => Reject();

    protected override void OnCancel()
    {
        Stop();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnContactRemoved(PointerContact contact) => Stop();

    private void Fire()
    {
        // будильник мог сработать уже после ухода пальца: Dispose не
        // догоняет вызов, уже стоящий в очереди UI, поэтому решает
        // проверка состояния, а не отмена таймера
        if (State != GestureState.Possible || Contact is not PointerContact contact) return;

        Accept();

        Triggered?.Invoke(this, new LongPressGestureEventArgs(contact.Location));
    }

    private void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }
}