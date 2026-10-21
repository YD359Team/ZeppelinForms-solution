using System.Collections.Concurrent;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>
/// The Linux platform over a Wayland compositor: xdg-shell windows drawn on the GPU
/// through EGL or into shared memory, the seat's pointer and keyboard.
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

    private WaylandSeat? _seat;
    private WaylandCursors? _cursors;
    private WlDataDeviceManager? _dataDeviceManager;
    private WaylandDataDevice? _dataDevice;

    private ZwpTextInputManagerV3? _textInputManager;

    private EglDisplay? _eglDisplay;
    private bool _eglTried;

    /// <summary>What the windows draw with.</summary>
    public IGraphicsBackend Graphics { get; }

    /// <summary>Nothing beyond the minimum: the compositor alone places windows.</summary>
    public PlatformCapabilities Capabilities => PlatformCapabilities.None;

    internal WlCompositor Compositor { get; }
    internal WlShm Shm { get; }
    internal XdgWmBase WmBase { get; }

    /// <summary>Set to "client" to have every window draw its own frame even where the
    /// compositor would draw one — as GTK_CSD=1 does for GTK.</summary>
    public const string DecorationsVariable = "ZF_WAYLAND_DECORATIONS";

    internal static bool ClientDecorationsForced => string.Equals(
        Environment.GetEnvironmentVariable(DecorationsVariable), "client", StringComparison.OrdinalIgnoreCase);

    /// <summary>xdg-decoration: the compositor draws the windows' frames when asked —
    /// KDE, Sway, Hyprland. Null where it never does — GNOME —, and the windows draw
    /// their own.</summary>
    internal ZxdgDecorationManagerV1? DecorationManager { get; private set; }

    /// <summary>The screens the compositor announced, by their global name.</summary>
    internal IReadOnlyCollection<WaylandOutput> Outputs => _outputs.Values;

    /// <summary>The user's keyboard and pointer; null on a compositor without a seat — a
    /// headless one, a kiosk with no input.</summary>
    internal WaylandSeat? Seat => _seat;

    /// <summary>The seat's input method; null where the compositor has none to relay.</summary>
    internal WaylandTextInput? TextInput { get; private set; }

    /// <summary>The cursor theme and the picture cursors, loaded on the first cursor shown.</summary>
    internal WaylandCursors Cursors => _cursors ??= new WaylandCursors(Shm);

    /// <summary>EGL over this connection, made on the first window's request; null when
    /// the machine has no libEGL or libwayland-egl, or EGL won't initialize — then the
    /// windows draw into shared memory.</summary>
    internal EglDisplay? EglDisplay
    {
        get
        {
            if (_eglTried) return _eglDisplay;

            _eglTried = true;

            // asked first: the absence of either library is no error, and spares the
            // DllNotFoundException in the debugger's output, as on X11
            if (!NativeProbe.Has(Egl.Lib) || !NativeProbe.Has(WaylandEglSurface.WaylandEglLib))
            {
                System.Diagnostics.Debug.WriteLine("No libEGL or libwayland-egl: the windows draw in shared memory");
                return null;
            }

            try
            {
                _eglDisplay = EglDisplay.ForWayland(_connection.Handle);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EGL is unavailable ({ex.Message}): the windows draw in shared memory");
            }

            return _eglDisplay;
        }
    }

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

                case "zxdg_decoration_manager_v1":
                    DecorationManager = Bind<ZxdgDecorationManagerV1>(name, version);
                    break;

                case "zwp_text_input_manager_v3":
                    _textInputManager = Bind<ZwpTextInputManagerV3>(name, version);
                    break;

                case "wl_data_device_manager":
                    _dataDeviceManager = Bind<WlDataDeviceManager>(name, version);
                    break;

                case "wl_seat" when _seat is null:
                    _seat = new WaylandSeat(this, Bind<WlSeat>(name, version));
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

        // the clipboard and the drops are the seat's: a compositor without either — a
        // kiosk, a headless one — keeps the clipboard there was
        if (_seat is not null && _dataDeviceManager is not null)
        {
            _dataDevice = new WaylandDataDevice(this, _seat, _dataDeviceManager);
            Clipboard.Current = _dataDevice;
        }

        // input methods: GNOME and KDE relay ibus and fcitx through text-input-v3; a
        // compositor without it leaves the windows the keyboard's own text, Compose included
        if (_seat is not null && _textInputManager is not null)
            TextInput = new WaylandTextInput(this, _textInputManager.GetTextInput(_seat.Proxy));

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

    /// <summary>The window of a surface an input event names; null for a surface that
    /// isn't a window's — the cursor's, another client's.</summary>
    internal WaylandWindow? FindWindow(WlSurface? surface)
    {
        if (surface is null) return null;

        foreach (WaylandWindow window in _windows)
            if (ReferenceEquals(window.Surface, surface))
                return window;

        return null;
    }

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
        _seat?.Forget(window);

        _dataDevice?.Forget(window);

        TextInput?.Forget(window);

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

    /// <summary>Send the requests made so far: before waiting on another client.</summary>
    internal void Flush() => _connection.Flush();

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

        // Wayland repeats no key: the held one is repeated here, when its time came
        if (_seat?.Keyboard is { } keyboard)
            App.RunGuarded(keyboard.DispatchRepeat);

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
        int timeout = _tickingWindows.Count > 0 ? _tickIntervalMs : 100;

        // a held key wakes the loop for its next repeat
        if (_seat?.Keyboard?.RepeatDueInMs is { } repeat)
            timeout = Math.Min(timeout, repeat);

        return timeout;
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