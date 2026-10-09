using ZeppelinForms.Dispatchers;

namespace ZeppelinForms.Core;

/// <summary>
/// Returns await continuations to the UI thread. Without it the async dialog model
/// breaks on desktop platforms: DestroyWindow delivers WM_DESTROY synchronously,
/// the wait completes, and the continuation after ShowDialogAsync goes to the
/// thread pool — where it touches the owner window from a foreign thread.
/// </summary>
public sealed class ZfSynchronizationContext : SynchronizationContext
{
    private readonly Dispatcher _dispatcher;

    public ZfSynchronizationContext(IPlatformWindow window)
    {
        // the window is created on the UI thread just before the context, so the
        // thread already has its dispatcher. Going through the dispatcher rather
        // than this one window keeps continuations running after the window
        // closes while others stay open
        _dispatcher = Dispatcher.Current ?? Dispatcher.Attach(window);
    }

    /// <summary>One context per UI thread, there is nothing to copy.</summary>
    public override SynchronizationContext CreateCopy() => this;

    public override void Post(SendOrPostCallback callback, object? state) =>
        _dispatcher.BeginInvoke(() => callback(state));

    // already on the UI thread the dispatcher runs the callback at once: there is
    // no point going through the queue, and waiting for it is a straight road to
    // a deadlock. From another thread it waits, and rethrows what the callback threw
    public override void Send(SendOrPostCallback callback, object? state) =>
        _dispatcher.Invoke(() => callback(state));
}