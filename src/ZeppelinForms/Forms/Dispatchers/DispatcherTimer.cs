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

    /// <summary>A tick is queued and not yet run: the elapses after it are dropped.</summary>
    private int _tickQueued;

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

        lock (_sync)
        {
            // stopped, or restarted since this pool timer was made: its elapse is
            // not one of the current run's, and would come early for it
            if (!_enabled || (run is { } r && r != _generation)) return;
            generation = _generation;
        }

        if (Interlocked.Exchange(ref _tickQueued, 1) == 1) return;

        Dispatcher.BeginInvoke(() => Deliver(generation));
    }

    /// <summary>On the UI thread: raise the tick if the run that queued it is still on.</summary>
    private void Deliver(int generation)
    {
        Volatile.Write(ref _tickQueued, 0);

        lock (_sync)
        {
            if (!_enabled || generation != _generation) return;
        }

        Tick?.Invoke(this, EventArgs.Empty);
    }

    private void OnOwnerClosed(object? sender, EventArgs e) => Stop();
}