using System.Runtime.ExceptionServices;

namespace ZeppelinForms.Dispatchers;

/// <summary>
/// The way to the UI thread from any other: a background load, a timer, a socket
/// callback. Elements and forms may be touched only on the thread that opened
/// their window; the dispatcher carries the work there.
/// </summary>
/// <remarks>
/// <para>
/// One dispatcher per thread that opens windows. <see cref="UIThread"/> is the one
/// of the application's thread — the thread App.Run was called on. A form's own,
/// <c>Form.Dispatcher</c>, is its thread's dispatcher going through the form's
/// window while it is open. An application with a single UI thread — nearly
/// every one — never sees the difference.
/// </para>
/// <para>
/// The work goes through the queue of one of the thread's open windows, so the
/// dispatcher runs as long as any of them is open. Work queued before the first
/// window, or between the last one closing and a new one opening, waits for it.
/// </para>
/// </remarks>
public sealed class Dispatcher
{
    [ThreadStatic]
    private static Dispatcher? t_current;

    private static readonly Lock s_lock = new();
    private static Dispatcher? s_uiThread;

    private readonly Lock _lock = new();
    private readonly List<IPlatformWindow> _windows = [];
    private readonly Queue<Action> _pending = new();

    // 0 — no thread yet: no window was opened anywhere
    private int _threadId;

    // a dispatcher whose thread was left without windows gives way to the next
    // UI thread, and whoever kept a reference to it is sent there
    private Dispatcher? _successor;

    // a form's dispatcher: the dispatcher of its thread, through its window
    private readonly Dispatcher? _owner;
    private readonly IPlatformWindow? _window;

    private Dispatcher() { }

    private Dispatcher(Dispatcher owner, IPlatformWindow window)
    {
        _owner = owner;
        _window = window;
    }

    /// <summary>The dispatcher that goes through the given window while it is open,
    /// and through the thread's other windows after it closes.</summary>
    internal Dispatcher ForWindow(IPlatformWindow window) => new(_owner ?? this, window);

    /// <summary>The dispatcher of the application's UI thread.</summary>
    public static Dispatcher UIThread
    {
        get
        {
            lock (s_lock)
                return s_uiThread ??= new Dispatcher();
        }
    }

    /// <summary>The dispatcher of the calling thread, if it opened windows.</summary>
    internal static Dispatcher? Current => t_current;

    /// <summary>Whether the calling thread is this dispatcher's thread. Before the
    /// first window there is no UI thread yet, and any thread is it.</summary>
    public bool CheckAccess()
    {
        if (_owner is not null)
            return _owner.CheckAccess();

        if (Volatile.Read(ref _successor) is { } successor)
            return successor.CheckAccess();

        int threadId = Volatile.Read(ref _threadId);
        return threadId == 0 || threadId == Environment.CurrentManagedThreadId;
    }

    /// <summary>Throw if the calling thread is not this dispatcher's thread.</summary>
    /// <exception cref="InvalidOperationException">Called from another thread.</exception>
    public void VerifyAccess()
    {
        if (!CheckAccess())
            throw new InvalidOperationException(
                "The calling thread cannot access this object because a different thread owns it. " +
                "Use Dispatcher.Invoke or Dispatcher.BeginInvoke.");
    }

    /// <summary>Run an action on the UI thread and wait for it. On the UI thread
    /// itself it runs at once: waiting for the queue there would never end.</summary>
    /// <remarks>An exception of the action is thrown here, on the calling thread,
    /// with its original stack.</remarks>
    /// <exception cref="InvalidOperationException">The UI thread has no open window
    /// to run the action through.</exception>
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Invoke(() =>
        {
            action();
            return true;
        });
    }

    /// <summary>Run a function on the UI thread and wait for its result.</summary>
    /// <exception cref="InvalidOperationException">The UI thread has no open window
    /// to run the function through.</exception>
    public T Invoke<T>(Func<T> function) => Invoke(function, via: null);

    /// <summary>Invoke through a particular window of this thread — a form's own.</summary>
    internal T Invoke<T>(Func<T> function, IPlatformWindow? via)
    {
        ArgumentNullException.ThrowIfNull(function);

        if (_owner is not null)
            return _owner.Invoke(function, via ?? _window);

        // a reference kept from before the application moved to another thread
        if (Volatile.Read(ref _successor) is { } successor)
            return successor.Invoke(function, via: null);

        if (CheckAccess())
            return function();

        T result = default!;
        ExceptionDispatchInfo? failure = null;

        using var completed = new ManualResetEventSlim(false);

        // with no window to go through the action would only be queued, and the
        // caller would wait for a window nobody is going to open
        if (!TryPost(Run, queueIfNoWindow: false, via))
            throw new InvalidOperationException("The UI thread has no open window to run the action on.");

        completed.Wait();
        failure?.Throw();

        return result;

        void Run()
        {
            try
            {
                result = function();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                completed.Set();
            }
        }
    }

    /// <summary>Queue an action to the UI thread and return at once. Always through
    /// the queue, even on the UI thread itself: the action runs after the current
    /// handler is done.</summary>
    public void BeginInvoke(Action action) => BeginInvoke(action, via: null);

    internal void BeginInvoke(Action action, IPlatformWindow? via)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_owner is not null)
            _owner.BeginInvoke(action, via ?? _window);
        else
            TryPost(action, queueIfNoWindow: true, via);
    }

    /// <summary>Queue an action to the UI thread; the task completes when it ran.</summary>
    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return InvokeAsync(() =>
        {
            action();
            return true;
        });
    }

    /// <summary>Queue a function to the UI thread; the task gives its result.</summary>
    public Task<T> InvokeAsync<T>(Func<T> function)
    {
        ArgumentNullException.ThrowIfNull(function);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        BeginInvoke(() =>
        {
            try
            {
                completion.SetResult(function());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });

        return completion.Task;
    }

    /// <summary>Queue asynchronous work to the UI thread; the task completes when
    /// the work does, not when it reaches its first await.</summary>
    public Task InvokeAsync(Func<Task> function)
    {
        ArgumentNullException.ThrowIfNull(function);

        return InvokeAsync(async () =>
        {
            await function();
            return true;
        });
    }

    /// <summary>Queue asynchronous work to the UI thread; the task gives its result.</summary>
    public Task<T> InvokeAsync<T>(Func<Task<T>> function)
    {
        ArgumentNullException.ThrowIfNull(function);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        BeginInvoke(async () =>
        {
            try
            {
                completion.SetResult(await function());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });

        return completion.Task;
    }

    // ===== Windows =====

    /// <summary>A window was created on the calling thread: the thread becomes
    /// a UI thread, and its dispatcher goes through the window.</summary>
    internal static Dispatcher Attach(IPlatformWindow window)
    {
        Dispatcher dispatcher = t_current ?? BindCurrentThread();
        dispatcher.Add(window);
        return dispatcher;
    }

    /// <summary>The window was destroyed and can't run anything anymore.</summary>
    internal void Detach(IPlatformWindow window)
    {
        if (_owner is not null)
        {
            _owner.Detach(window);
            return;
        }

        lock (_lock)
            _windows.Remove(window);
    }

    private static Dispatcher BindCurrentThread()
    {
        lock (s_lock)
        {
            int threadId = Environment.CurrentManagedThreadId;

            // the first thread to open a window is the UI thread. Work queued
            // before it waits in the dispatcher that already exists, so that
            // dispatcher is bound rather than replaced
            if (s_uiThread is null || s_uiThread._threadId == 0)
            {
                s_uiThread ??= new Dispatcher();
                Volatile.Write(ref s_uiThread._threadId, threadId);
            }
            else if (!s_uiThread.HasWindows)
            {
                // the old UI thread has no windows left — the application started
                // again on another thread. Its queue moves along with the title
                var next = new Dispatcher { _threadId = threadId };
                s_uiThread.HandOver(next);
                s_uiThread = next;
            }
            else
            {
                // a second UI thread next to a living first one: it gets a
                // dispatcher of its own, and UIThread stays where it is
                t_current = new Dispatcher { _threadId = threadId };
                return t_current;
            }

            t_current = s_uiThread;
            return s_uiThread;
        }
    }

    private bool HasWindows
    {
        get
        {
            lock (_lock)
                return _windows.Count > 0;
        }
    }

    private void HandOver(Dispatcher next)
    {
        lock (_lock)
        {
            Volatile.Write(ref _successor, next);

            while (_pending.TryDequeue(out Action? action))
                next._pending.Enqueue(action);
        }
    }

    private void Add(IPlatformWindow window)
    {
        Action[] pending;

        lock (_lock)
        {
            if (!_windows.Contains(window))
                _windows.Add(window);

            pending = [.. _pending];
            _pending.Clear();
        }

        // in the order they were queued
        foreach (Action action in pending)
            window.Invoke(action);
    }

    private bool TryPost(Action action, bool queueIfNoWindow, IPlatformWindow? via)
    {
        IPlatformWindow? window;
        Dispatcher? successor;

        lock (_lock)
        {
            successor = _successor;

            // the window asked for — a form invoking through its own — while it is
            // open; otherwise the oldest one: the main window outlives its dialogs
            window = successor is not null ? null
                : via is not null && _windows.Contains(via) ? via
                : _windows.Count > 0 ? _windows[0]
                : null;

            if (successor is null && window is null)
            {
                if (!queueIfNoWindow) return false;

                _pending.Enqueue(action);
                return true;
            }
        }

        if (successor is not null)
            return successor.TryPost(action, queueIfNoWindow, via: null);

        window!.Invoke(action);
        return true;
    }
}