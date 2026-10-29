using ZeppelinForms.Forms;

namespace ZeppelinForms.Dispatchers;

/// <summary>
/// A timer whose <see cref="Tick"/> comes on the UI thread, as WinForms' Timer and
/// WPF's DispatcherTimer: a clock in the status bar, a poll of a process list, an
/// autosave — no marshalling, no Invoke in the handler.
/// </summary>
/// <remarks>
/// <para>
/// The time is kept by a pool timer, and each tick is queued to the dispatcher. Ticks
/// don't pile up: while one is queued and not yet run, the next one is dropped — a
/// UI thread busy for a second gets one tick after it, not sixty in a row. The
/// interval is measured from the start, not from the end of the last handler, so a
/// slow handler doesn't stretch the cadence.
/// </para>
/// <para>
/// A timer made for a form (<see cref="DispatcherTimer(Form)"/>) goes through the
/// form's dispatcher and stops when the form closes: a tick that reached a closed
/// form's controls was the classic bug of a timer started in Load and forgotten.
/// </para>
/// <para>
/// Start, Stop and the properties may be used from any thread. A tick queued before
/// Stop doesn't run after it, and one queued before a restart doesn't count for the
/// new run.
/// </para>
/// <para>
/// A queued tick that never runs — the window it was posted through closed first, the
/// dispatcher stopped taking work — doesn't stop the timer: a restart starts with
/// nothing queued, and within a run a tick queued longer than a second is taken for
/// lost, and the next elapse queues another. If the lost one comes after all, it
/// does nothing: only the latest queued tick raises <see cref="Tick"/>, so a busy UI
/// thread still gets one tick after it, not two.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var clock = new DispatcherTimer(this) { Interval = TimeSpan.FromSeconds(1) };
/// clock.Tick += (_, _) => status.Text = DateTime.Now.ToLongTimeString();
/// clock.Start();
/// </code>
/// </example>
public sealed class DispatcherTimer : IDisposable
{
    private readonly Lock _sync = new();

    private readonly Dispatcher? _dispatcher;
    private readonly Form? _owner;

    private Timer? _timer;
    private TimeSpan _interval = TimeSpan.FromMilliseconds(100);
    private bool _enabled;
    private bool _disposed;

    /// <summary>Which run of the timer is the current one. Every Start and Stop moves
    /// it on; a tick queued under an older run is dropped when it comes.</summary>
    private int _generation;

    /// <summary>The ticket of the tick queued and not yet run; 0 — none. While there is
    /// one, the elapses after it are dropped.</summary>
    /// <remarks>
    /// A ticket rather than a flag: a flag set by a tick that never came stayed set
    /// for good — Stop didn't clear it, and every Start after it dropped all ticks in
    /// silence. Now Stop and every new run clear it, and a tick whose ticket is no
    /// longer the queued one — queued before a restart, or taken for lost and queued
    /// again — does nothing when it comes.
    /// </remarks>
    private long _queuedTicket;

    /// <summary>The last ticket given out: tickets never repeat, so a stale tick is
    /// never taken for the current one.</summary>
    private long _lastTicket;

    /// <summary>When the queued tick was queued, by <see cref="Environment.TickCount64"/>.</summary>
    private long _queuedAt;

    /// <summary>A timer of the calling thread's dispatcher, or of the UI thread's when
    /// the calling thread has opened no window yet — the usual case for a timer made
    /// before App.Run.</summary>
    public DispatcherTimer()
    {
        _dispatcher = Dispatcher.Current ?? Dispatcher.UIThread;
    }

    /// <summary>A timer of a form: it ticks through the form's dispatcher and stops
    /// when the form closes.</summary>
    public DispatcherTimer(Form owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        _owner = owner;
        owner.Closed += OnOwnerClosed;
    }

    /// <summary>A timer with its interval and handler, not started.</summary>
    public DispatcherTimer(TimeSpan interval, EventHandler tick) : this()
    {
        ArgumentNullException.ThrowIfNull(tick);

        Interval = interval;
        Tick += tick;
    }

    /// <summary>Raised on the UI thread every <see cref="Interval"/> while the timer
    /// is enabled.</summary>
    public event EventHandler? Tick;

    /// <summary>The time between ticks; 100 ms by default. A change while the timer
    /// runs starts the interval anew.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Less than a millisecond, or more
    /// than <see cref="int.MaxValue"/> milliseconds.</exception>
    public TimeSpan Interval
    {
        get
        {
            lock (_sync)
                return _interval;
        }
        set
        {
            if (value < TimeSpan.FromMilliseconds(1) || value.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "The interval must be at least a millisecond and at most int.MaxValue milliseconds.");

            lock (_sync)
            {
                if (_interval == value) return;

                _interval = value;

                if (_enabled) Arm();
            }
        }
    }

    /// <summary>Whether the timer ticks: <see cref="Start"/> and <see cref="Stop"/>,
    /// as a property — WinForms' Enabled.</summary>
    public bool IsEnabled
    {
        get
        {
            lock (_sync)
                return _enabled;
        }
        set
        {
            if (value) Start();
            else Stop();
        }
    }

    /// <summary>Anything the application wants to keep with the timer.</summary>
    public object? Tag { get; set; }

    /// <summary>How long a queued tick may stay unrun before it is taken for lost and
    /// another one is queued; a second by default. A test sets it to zero to lose
    /// a tick on purpose.</summary>
    /// <remarks>
    /// Not a cadence: a UI thread busy for a minute gets a few dozen empty queue items,
    /// one per second, and a single <see cref="Tick"/> after it — the superseded
    /// tickets do nothing. Short enough that a lost tick costs about a second of
    /// silence, long enough that a hung thread doesn't flood its queue.
    /// </remarks>
    internal TimeSpan LostTickAfter { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The dispatcher the ticks come through: the owner form's while it has
    /// one, otherwise the one the timer was made on.</summary>
    public Dispatcher Dispatcher => _owner?.Dispatcher ?? _dispatcher ?? Dispatcher.UIThread;

    /// <summary>Start ticking; the first tick comes one interval from now. Starting a
    /// running timer starts its interval anew.</summary>
    /// <exception cref="ObjectDisposedException">The timer was disposed.</exception>
    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _enabled = true;
            Arm();
        }
    }

    /// <summary>Stop ticking. A tick already queued doesn't come.</summary>
    public void Stop()
    {
        lock (_sync)
        {
            if (!_enabled) return;

            _enabled = false;
            _generation++;

            // a tick still on its way — or lost on it — must not hold back the next Start
            _queuedTicket = 0;

            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    /// <summary>Stop the timer and let its pool timer go. A disposed timer can't be started again.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;

            _disposed = true;
            _enabled = false;
            _generation++;
            _queuedTicket = 0;

            _timer?.Dispose();
            _timer = null;
        }

        if (_owner is not null)
            _owner.Closed -= OnOwnerClosed;
    }

    /// <summary>Called under <see cref="_sync"/>: a new run of the pool timer, one
    /// interval from now.</summary>
    private void Arm()
    {
        _generation++;

        // a new run starts with nothing queued: a tick of the old one is dropped when
        // it comes, and must not drop the new run's ticks while it waits
        _queuedTicket = 0;

        // the timer's state is the generation it ticks for: a callback of the old run
        // that is already on its way is told apart from the new one
        _timer?.Dispose();
        _timer = new Timer(
            static state =>
            {
                var (timer, generation) = ((DispatcherTimer, int))state!;
                timer.Elapse(generation);
            },
            (this, _generation),
            _interval,
            _interval);
    }

    /// <summary>The interval elapsed — on a pool thread, or in a test that drives the
    /// timer by hand: queue a tick unless one is queued already.</summary>
    /// <param name="run">The run the pool timer ticks for; null — the current one.</param>
    internal void Elapse(int? run = null)
    {
        int generation;
        long ticket;

        lock (_sync)
        {
            // stopped, or restarted since this pool timer was made: its elapse is
            // not one of the current run's, and would come early for it
            if (!_enabled || (run is { } r && r != _generation)) return;

            long now = Environment.TickCount64;

            // a tick is on its way: this elapse is dropped — unless the tick has been
            // on its way so long that it is taken for lost
            if (_queuedTicket != 0 && now - _queuedAt < (long)LostTickAfter.TotalMilliseconds)
                return;

            generation = _generation;
            ticket = ++_lastTicket;

            _queuedTicket = ticket;
            _queuedAt = now;
        }

        try
        {
            Dispatcher.BeginInvoke(() => Deliver(generation, ticket));
        }
        catch (InvalidOperationException)
        {
            // a dispatcher that takes no work any more: the tick wasn't queued, so it
            // must not be waited for. On a pool thread the exception would take the
            // process down for a tick nobody can receive
            lock (_sync)
            {
                if (_queuedTicket == ticket) _queuedTicket = 0;
            }
        }
    }

    /// <summary>On the UI thread: raise the tick if it is still the queued one and the
    /// run that queued it is still on.</summary>
    private void Deliver(int generation, long ticket)
    {
        lock (_sync)
        {
            // superseded: queued before a Stop or a restart, or taken for lost and
            // queued again — the newer one raises the tick, this one doesn't
            if (ticket != _queuedTicket) return;

            _queuedTicket = 0;

            if (!_enabled || generation != _generation) return;
        }

        Tick?.Invoke(this, EventArgs.Empty);
    }

    private void OnOwnerClosed(object? sender, EventArgs e) => Stop();
}