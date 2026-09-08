namespace ZeppelinForms.Core;

/// <summary>
/// Возвращает продолжения await в поток UI. Без этого async-модель диалогов
/// ломается на настольных платформах: DestroyWindow доставляет WM_DESTROY
/// синхронно, ожидание завершается, и продолжение после ShowDialogAsync
/// уходит в пул потоков — а там оно трогает окно владельца чужим потоком.
/// </summary>
public sealed class ZfSynchronizationContext : SynchronizationContext
{
    private readonly IPlatformWindow _window;
    private readonly int _threadId;

    public ZfSynchronizationContext(IPlatformWindow window)
    {
        _window = window;
        _threadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>Контекст один на поток UI, копировать нечего.</summary>
    public override SynchronizationContext CreateCopy() => this;

    public override void Post(SendOrPostCallback callback, object? state) =>
        _window.Invoke(() => callback(state));

    public override void Send(SendOrPostCallback callback, object? state)
    {
        // уже в потоке UI: идти через очередь незачем, а ждать её —
        // прямой путь к взаимной блокировке
        if (Environment.CurrentManagedThreadId == _threadId)
        {
            callback(state);
            return;
        }

        using var completed = new ManualResetEventSlim(false);
        Exception? failure = null;

        _window.Invoke(() =>
        {
            try
            {
                callback(state);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        });

        completed.Wait();

        if (failure is not null)
            throw failure;
    }
}