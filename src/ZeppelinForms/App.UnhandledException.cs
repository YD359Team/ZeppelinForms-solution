using ZeppelinForms.Core;

namespace ZeppelinForms;

/// <summary>An exception that escaped a step of the UI loop.</summary>
/// <param name="Exception">What was thrown, with its original stack.</param>
public sealed record class DispatcherUnhandledExceptionEventArgs(Exception Exception) : ZfEventArgs
{
    /// <summary>true — the exception is dealt with: the loop carries on and the rest
    /// of the failed step is skipped. false, the default — it goes on as if nobody
    /// had looked at it, and the application ends as it would have without the event.</summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Exceptions that escape the UI loop: an event handler, an action queued to the
/// dispatcher, an await continuation and an async void method, an animation step,
/// deferred work, layout and painting.
/// </summary>
/// <remarks>
/// The step is guarded where the platform hands it to the framework: one window
/// message, one X11 event, one queued action, one animation, one deferred job.
/// The guard is an exception filter that is true only while somebody listens: without
/// a handler the exception flies on exactly as before, with the same stack, and a
/// debugger stops where it was thrown.
/// </remarks>
public partial class App
{
    /// <summary>The last exception a handler declined, on this thread. A step guarded
    /// inside another one — a queued action inside a window message — rethrows what
    /// was declined, and the outer guard must not offer it a second time.</summary>
    [ThreadStatic]
    private static Exception? t_declined;

    /// <summary>An exception escaped a step of the UI loop. Raised on the UI thread,
    /// sender null. Set Handled to keep the application running; leave it to let the
    /// exception end it, as it would without a handler.</summary>
    public static event EventHandler<DispatcherUnhandledExceptionEventArgs>? UnhandledException;

    /// <summary>Whether anybody listens. The guards' filter: without a listener the
    /// exception is not caught at all.</summary>
    internal static bool HasUnhandledExceptionHandler => UnhandledException is not null;

    /// <summary>Offer an exception to <see cref="UnhandledException"/>.</summary>
    /// <returns>true — handled, the caller carries on; false — the caller rethrows.</returns>
    internal static bool OfferUnhandledException(Exception exception)
    {
        // declined further down the stack: the answer is already known
        if (ReferenceEquals(exception, t_declined)) return false;

        EventHandler<DispatcherUnhandledExceptionEventArgs>? handler = UnhandledException;
        if (handler is null) return false;

        var args = new DispatcherUnhandledExceptionEventArgs(exception);
        handler(null, args);

        if (!args.Handled) t_declined = exception;

        return args.Handled;
    }

    /// <summary>Run one step of the UI loop. An exception that escapes it is offered
    /// to <see cref="UnhandledException"/>; declined or unheard, it flies on.</summary>
    internal static void RunGuarded(Action step)
    {
        try
        {
            step();
        }
        catch (Exception exception) when (HasUnhandledExceptionHandler)
        {
            if (!OfferUnhandledException(exception)) throw;
        }
    }
}