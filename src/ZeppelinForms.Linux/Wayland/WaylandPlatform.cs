using System.Collections.Concurrent;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>
/// The Linux platform over a Wayland compositor: xdg-shell windows drawn into shared
/// memory.
/// </summary>
/// <remarks>
/// <para>
/// Wayland lets no window place itself: <see cref="Capabilities"/> has no
/// <see cref="PlatformCapabilities.WindowPositioning"/>, and setting <c>Form.Position</c>
/// of an open window, centering it or opening it with
/// <see cref="Forms.Enums.WindowStartupLocation.Manual"/> throws
/// <see cref="PlatformNotSupportedException"/>. CenterScreen and CenterOwner are left to the
/// compositor: an owned form is made a child of its owner's window, and compositors put
/// those over the owner.
/// </para>
/// <para>
/// <see cref="LinuxPlatform.Create()"/> chooses between this platform and
/// <see cref="X11Platform"/> by the session.
/// </para>
/// </remarks>
public sealed class WaylandPlatform : IPlatform, INestedLoopSupport
{
    private readonly WaylandConnection _connection;
    private readonly WlRegistry _registry;
    private readonly List<WaylandWindow> _windows = [];
    private readonly ConcurrentQueue<Action> _invokeQueue = new();
    private readonly EventFd _wake = new();
    private readonly Dictionary<uint, WaylandOutput> _outputs = [];

    private PortalAppearance? _appearance;
    private bool _running;

    /// <summary>What the windows draw with.</summary>
    public IGraphicsBackend Graphics { get; }

    /// <summary>Nothing beyond the minimum: the compositor alone places windows.</summary>
    public PlatformCapabilities Capabilities => PlatformCapabilities.None;

    internal WlCompositor Compositor { get; }
    internal WlShm Shm { get; }
    internal XdgWmBase WmBase { get; }

    /// <summary>The screens the compositor announced, by their global name.</summary>
    internal IReadOnlyCollection<WaylandOutput> Outputs => _outputs.Values;

    /// <summary>The platform drawing with Skia.</summary>
    public WaylandPlatform() : this(Skia.SkiaGraphicsBackend.Instance)
    {
    }

    /// <summary>The platform drawing with <paramref name="graphics"/>.</summary>
    /// <exception cref="ZfPlatformException">No compositor answered, or it lacks what a
    /// window needs: wl_compositor, wl_shm, xdg_wm_base.</exception>
    public WaylandPlatform(IGraphicsBackend graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        Graphics = graphics;

        _connection = WaylandConnection.Connect();
        _registry = _connection.Display.GetRegistry();

        WlCompositor? compositor = null;
        WlShm? shm = null;
        XdgWmBase? wmBase = null;

        _registry.Global += (name, interfaceName, version) =>
        {
            switch (interfaceName)
            {
                case "wl_compositor":
                    compositor = Bind<WlCompositor>(name, version);
                    break;

                case "wl_shm":
                    shm = Bind<WlShm>(name, version);
                    break;

                case "xdg_wm_base":
                    wmBase = Bind<XdgWmBase>(name, version);
                    break;

                case "wl_output":
                    // a screen plugged in while the application runs comes here too
                    _outputs[name] = new WaylandOutput(Bind<WlOutput>(name, version));
                    break;
            }
        };

        _registry.GlobalRemove += name =>
        {
            if (_outputs.Remove(name, out WaylandOutput? output))
                output.Dispose();
        };

        // the first round brings the globals, the second what the bound ones say at once:
        // the outputs' scales and modes
        _connection.Roundtrip();
        _connection.Roundtrip();

        if (compositor is null || shm is null || wmBase is null)
        {
            string missing = string.Join(", ", new[]
            {
                compositor is null ? "wl_compositor" : null,
                shm is null ? "wl_shm" : null,
                wmBase is null ? "xdg_wm_base" : null,
            }.OfType<string>());

            _connection.Dispose();

            throw new ZfPlatformException($"The Wayland compositor lacks {missing}: no window can be made there.");
        }

        Compositor = compositor;
        Shm = shm;
        WmBase = wmBase;

        // a client that doesn't answer is marked as hung by the compositor
        WmBase.Ping += serial => WmBase.Pong(serial);

        Displays.Current = new WaylandDisplayProvider(this);

        graphics.Register();

        _appearance = PortalAppearance.TryCreate();

        if (_appearance is not null)
            App.UseSystemAppearance(_appearance);
    }

    private T Bind<T>(uint name, uint version) where T : WlProxy, IWlObject<T> =>
        _registry.Bind<T>(name, Math.Min(version, T.InterfaceVersion));

    /// <summary>The largest scale of the screens: what a window is drawn at before the
    /// compositor says which screen it is on.</summary>
    internal int LargestOutputScale => _outputs.Count == 0 ? 1 : _outputs.Values.Max(o => o.Scale);

    internal WaylandOutput? FindOutput(WlOutput? proxy) =>
        proxy is null ? null : _outputs.Values.FirstOrDefault(o => ReferenceEquals(o.Proxy, proxy));

    public IPlatformWindow CreateWindow(Form form)
    {
        var window = new WaylandWindow(this, form);
        window.Create();
        form.PlatformWindow = window;
        form.Platform = this;
        return window;
    }

    internal void Register(WaylandWindow window) => _windows.Add(window);

    internal void Unregister(WaylandWindow window)
    {
        _windows.Remove(window);

        if (_windows.Count == 0)
            _running = false;
    }

    public void RunNestedLoop(IPlatformWindow until)
    {
        var dialogWindow = (WaylandWindow)until;

        while (dialogWindow.IsAlive)
            PumpOnce();
    }

    private readonly HashSet<WaylandWindow> _tickingWindows = [];
    private int _tickIntervalMs = 16;
    private long _lastTickTicks;

    internal void StartTicking(WaylandWindow window, int intervalMs)
    {
        _tickIntervalMs = intervalMs;

        if (_tickingWindows.Count == 0)
            _lastTickTicks = Environment.TickCount64;

        _tickingWindows.Add(window);
    }

    internal void StopTicking(WaylandWindow window) => _tickingWindows.Remove(window);

    public void Start()
    {
        _running = true;

        while (_running)
            PumpOnce();
    }

    public void Exit()
    {
        _running = false;

        foreach (WaylandWindow window in _windows.ToList())
            window.Close();

        _connection.Flush();
    }

    /// <summary>Run an action on the loop's thread: from any thread.</summary>
    internal void Invoke(Action action)
    {
        _invokeQueue.Enqueue(action);
        _wake.Signal();
    }

    /// <summary>
    /// One turn of the loop: wait for the compositor, a wake-up, the session bus or the
    /// next tick; dispatch the events; run what was invoked; tick; paint.
    /// </summary>
    /// <remarks>
    /// The read goes the way libwayland asks a client with a loop of its own: prepare the
    /// read — which fails while events are queued, and they are dispatched first — flush,
    /// poll, then read or cancel. Reading the socket without the preparation can lose the
    /// events another reader takes in between.
    /// </remarks>
    private void PumpOnce()
    {
        nint display = _connection.Handle;

        while (WaylandNative.wl_display_prepare_read(display) != 0)
            DispatchPending();

        _connection.Flush();

        Span<Poll.Entry> entries = stackalloc Poll.Entry[3];
        entries[0] = new Poll.Entry { Fd = _connection.FileDescriptor, Events = Poll.In };
        entries[1] = new Poll.Entry { Fd = _wake.FileDescriptor, Events = Poll.In };
        entries[2] = new Poll.Entry { Fd = _appearance?.FileDescriptor ?? -1, Events = Poll.In };

        Poll.Wait(entries, WaitTimeout());

        // the compositor went away — it crashed, or the session ended: nothing more
        // will come, and waiting on would spin
        if ((entries[0].Returned & (Poll.Error | Poll.HangUp)) != 0 && (entries[0].Returned & Poll.In) == 0)
        {
            WaylandNative.wl_display_cancel_read(display);

            throw new ZfPlatformException("The Wayland compositor closed the connection.");
        }

        if ((entries[0].Returned & Poll.In) != 0)
        {
            if (WaylandNative.wl_display_read_events(display) < 0)
                throw new ZfPlatformException(
                    $"The connection to the Wayland compositor failed (error {WaylandNative.wl_display_get_error(display)}).");
        }
        else
        {
            WaylandNative.wl_display_cancel_read(display);
        }

        DispatchPending();

        if ((entries[1].Returned & Poll.In) != 0)
            _wake.Drain();

        // each action on its own, as on X11: one that threw doesn't hold the rest
        while (_invokeQueue.TryDequeue(out Action? action))
            App.RunGuarded(action);

        App.RunGuarded(() => _appearance?.Poll());

        DispatchTick();

        // after the input and the tick, so that what they changed goes into one frame;
        // each window on its own, over a copy — painting may close one
        foreach (WaylandWindow window in _windows.ToList())
            App.RunGuarded(window.PaintIfRequested);

        _connection.Flush();
    }

    /// <summary>Dispatch the queued events. What a handler threw goes to
    /// App.UnhandledException, as an X11 event's does.</summary>
    private void DispatchPending()
    {
        try
        {
            _connection.DispatchPending();
        }
        catch (Exception exception) when (exception is not ZfPlatformException && App.HasUnhandledExceptionHandler)
        {
            if (!App.OfferUnhandledException(exception)) throw;
        }
    }

    private int WaitTimeout()
    {
        // a frame that can go now goes without waiting for input
        foreach (WaylandWindow window in _windows)
            if (window.CanPaintNow)
                return 0;

        if (!_invokeQueue.IsEmpty) return 0;

        // with animations the loop wakes for the next tick; without them it waits for
        // input, the wake-up or the bus — at most 100 ms, as the X11 loop does
        return _tickingWindows.Count > 0 ? _tickIntervalMs : 100;
    }

    private void DispatchTick()
    {
        if (_tickingWindows.Count == 0) return;

        long now = Environment.TickCount64;
        if (now - _lastTickTicks < _tickIntervalMs) return;

        _lastTickTicks = now;

        foreach (WaylandWindow window in _tickingWindows.ToList())
            App.RunGuarded(window.RaiseTick);
    }
}