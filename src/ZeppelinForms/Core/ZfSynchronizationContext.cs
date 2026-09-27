namespace ZeppelinForms.Core;

/// <summary>
/// Returns await continuations to the UI thread. Without it the async dialog model
/// breaks on desktop platforms: DestroyWindow delivers WM_DESTROY synchronously,
/// the wait completes, and the continuation after ShowDialogAsync goes to the
/// thread pool — where it touches the owner window from a foreign thread.
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

    /// <summary>One context per UI thread, there is nothing to copy.</summary>
    public override SynchronizationContext CreateCopy() => this;

    public override void Post(SendOrPostCallback callback, object? state) =>
        _window.Invoke(() => callback(state));

    public override void Send(SendOrPostCallback callback, object? state)
    {
        // already on the UI thread: there is no point going through the queue,
        // and waiting for it is a straight road to a deadlock
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