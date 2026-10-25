using System.Runtime.InteropServices;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Linux;

public sealed partial class X11Platform : IPlatform, INestedLoopSupport
{
    private readonly Dictionary<nuint, X11Window> _windows = [];
    private bool _running;

    internal nint Display { get; private set; }

    /// <summary>What the windows draw with.</summary>
    public IGraphicsBackend Graphics { get; }

    /// <summary>X11 lets a window place itself: the window manager is asked and obeys.</summary>
    public PlatformCapabilities Capabilities => PlatformCapabilities.WindowPositioning | PlatformCapabilities.WindowHiding;

    private EglDisplay? _eglDisplay;
    private bool _eglTried;

    /// <summary>EGL over this connection, made on the first window's request; null
    /// when the machine has none or it won't initialize — then the windows draw in
    /// memory, as before 0.18.</summary>
    internal EglDisplay? EglDisplay
    {
        get
        {
            if (_eglTried) return _eglDisplay;

            _eglTried = true;
            // no libEGL is no error: the machine draws in memory, and asking the
            // library first spares the DllNotFoundException in the debugger's output
            if (!NativeProbe.Has(Egl.Lib))
            {
                System.Diagnostics.Debug.WriteLine($"No {Egl.Lib}: the windows draw in memory");
                return null;
            }

            try
            {
                _eglDisplay = EglDisplay.ForX11(Display);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EGL is unavailable ({ex.Message}): the windows draw in memory");
            }

            return _eglDisplay;
        }
    }

    /// <summary>The platform drawing with Skia.</summary>
    public X11Platform() : this(Skia.SkiaGraphicsBackend.Instance)
    {
    }

    /// <summary>The platform drawing with <paramref name="graphics"/>: its services are
    /// registered here, and every window draws through its surfaces — on the GPU
    /// through EGL and OpenGL ES where the machine has them, into an XImage otherwise.</summary>
    /// <remarks>ZF_RENDERING=software in the environment keeps every window in memory
    /// even where EGL works: for a driver that draws wrong.</remarks>
    public X11Platform(IGraphicsBackend graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        Graphics = graphics;

        Display = X11.XOpenDisplay(0);

        if (Display == 0)
            throw new ZfPlatformException(
                "Could not connect to the X server. Check the DISPLAY variable.");

        // a protocol error is reported, not fatal: Xlib's own handler ends the process
        X11ErrorHandler.Install();

        // a held key reports isRepeat instead of a release and a press per repeat
        // (see X11Platform.Keyboard)
        EnableDetectableAutoRepeat();

        _inputMethod = OpenInputMethod(Display);

        graphics.Register();

        InitAppearance();
    }

    private X11Clipboard? _clipboard;

    /// <summary>The clipboard before this platform's: back in place when its last window
    /// closes — a clipboard working through a destroyed window can't be used.</summary>
    private IClipboard? _previousClipboard;

    /// <summary>The X input method, or 0 if none could be opened — then text
    /// falls back to XLookupString, which knows only Latin-1.</summary>
    private readonly nint _inputMethod;

    /// <summary>An input context for a window: the input method needs one per
    /// window to know where the typed text goes. 0 — no input method.</summary>
    internal nint CreateInputContext(nuint window)
    {
        if (_inputMethod == 0) return 0;

        // over the spot: the input method draws the composition itself, at the spot
        // the window names — the caret of the focused field, see X11Window.SetTextInputArea.
        // An input method that can't is asked for nothing, as before
        nint overTheSpot = CreateOverTheSpotContext(window);
        if (overTheSpot != 0) return overTheSpot;

        return X11.XCreateIC(
            _inputMethod,
            "inputStyle", X11.XIMPreeditNothing | X11.XIMStatusNothing,
            "clientWindow", window,
            "focusWindow", window,
            0);
    }

    /// <summary>The font set over-the-spot compositions are drawn in; 0 — none could be made.</summary>
    private nint _imeFontSet = -1;

    private nint CreateOverTheSpotContext(nuint window)
    {
        if (_imeFontSet == -1)
        {
            // any font of any charset: the input method only needs something to draw with
            _imeFontSet = X11.XCreateFontSet(
                Display, "-*-*-medium-r-normal--*-*-*-*-*-*-*-*,*", out nint missing, out _, out _);

            if (missing != 0) X11.XFreeStringList(missing);
        }

        if (_imeFontSet == 0) return 0;

        // the spot itself is set when a field reports its caret
        nint spot = Marshal.AllocHGlobal(4);

        try
        {
            Marshal.WriteInt32(spot, 0);

            nint attributes = X11.XVaCreateNestedList(0, "spotLocation", spot, "fontSet", _imeFontSet, 0);
            if (attributes == 0) return 0;

            try
            {
                return X11.XCreateIC(
                    _inputMethod,
                    "inputStyle", X11.XIMPreeditPosition | X11.XIMStatusNothing,
                    "clientWindow", window,
                    "focusWindow", window,
                    "preeditAttributes", attributes,
                    0);
            }
            finally
            {
                X11.XFree(attributes);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(spot);
        }
    }

    private static nint OpenInputMethod(nint display)
    {
        // the input method works in the current locale; the C locale that a process
        // starts in knows no non-Latin input at all
        X11.SetLocale(X11.LC_ALL, string.Empty);
        X11.XSetLocaleModifiers(string.Empty);

        nint inputMethod = X11.XOpenIM(display, 0, 0, 0);

        if (inputMethod != 0) return inputMethod;

        // XMODIFIERS may name an input method server that isn't running (a leftover
        // ibus or fcitx setting): the built-in one still handles layouts and Compose
        X11.XSetLocaleModifiers("@im=none");

        return X11.XOpenIM(display, 0, 0, 0);
    }

    internal void Register(X11Window window)
    {
        _windows[window.Handle] = window;

        // the clipboard needs an owner window, so it is created
        // not in the platform's constructor but together with the first window
        if (_clipboard is null)
        {
            _clipboard = new X11Clipboard(Display, window.Handle);
            _previousClipboard = Clipboard.Current;
            Clipboard.Current = _clipboard;
        }
    }

    // the slots of native controls, by their windows: their focus events come here
    private readonly Dictionary<nuint, X11NativeSlot> _slots = [];

    internal void RegisterSlot(nuint window, X11NativeSlot slot) => _slots[window] = slot;

    internal void UnregisterSlot(nuint window) => _slots.Remove(window);

    internal X11NativeSlot? SlotOf(nuint window) => _slots.GetValueOrDefault(window);

    internal bool HasSlots => _slots.Count > 0;

    internal void Unregister(X11Window window)
    {
        _windows.Remove(window.Handle);

        // the clipboard worked through this window: it moves to one still open, or,
        // with none left, steps aside for the clipboard there was before
        if (_clipboard is not null && _clipboard.Window == window.Handle)
        {
            if (_windows.Count > 0)
            {
                _clipboard.MoveTo(_windows.Keys.First());
            }
            else
            {
                if (ReferenceEquals(Clipboard.Current, _clipboard) && _previousClipboard is not null)
                    Clipboard.Current = _previousClipboard;

                _clipboard = null;
                _previousClipboard = null;
            }
        }

        if (_windows.Count == 0)
            _running = false;
    }

    public IPlatformWindow CreateWindow(Form form)
    {
        var window = new X11Window(this, form);
        window.Create();
        form.PlatformWindow = window;
        form.Platform = this;
        return window;
    }

    public void RunNestedLoop(IPlatformWindow until)
    {
        var dialogWindow = (X11Window)until;

        // the nested loop: spins while the dialog's window is alive
        while (dialogWindow.Handle != 0 && _windows.ContainsKey(dialogWindow.Handle))
            PumpOnce();
    }

    private readonly HashSet<X11Window> _tickingWindows = [];
    private int _tickIntervalMs = 16;
    private long _lastTickTicks;

    internal void StartTicking(X11Window window, int intervalMs)
    {
        _tickIntervalMs = intervalMs;

        if (_tickingWindows.Count == 0)
            _lastTickTicks = Environment.TickCount64;

        _tickingWindows.Add(window);
    }

    internal void StopTicking(X11Window window) => _tickingWindows.Remove(window);

    public void Start()
    {
        _running = true;

        while (_running)
            PumpOnce();
    }

    private void PumpOnce()
    {
        WaitForEventOrTimeout();

        // handle everything that has accumulated, without blocking
        while (X11.XPending(Display) > 0)
        {
            nint buffer = Marshal.AllocHGlobal(192);

            try
            {
                X11.XNextEvent(Display, buffer);

                // the input method sees every event first: a dead key or a Compose
                // sequence is its business, and the key press it took must not
                // reach the form as well
                if (_inputMethod != 0 && X11.XFilterEvent(buffer, 0))
                    continue;

                // one event is one step of the loop: what escapes it goes to
                // App.UnhandledException. Inline rather than RunGuarded: this is the
                // hottest path of the loop, and a lambda per event is not needed here
                try
                {
                    Dispatch(buffer);
                }
                catch (Exception exception) when (App.HasUnhandledExceptionHandler)
                {
                    if (!App.OfferUnhandledException(exception)) throw;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // the session bus: the portal's signals, handled on this thread
        App.RunGuarded(PollAppearance);

        DispatchTick();

        // one frame per pump for every window that asked for one: after the input
        // and the tick, so that everything they changed goes into the same frame.
        // A copy — painting a window must not trip over a window closing.
        // Each window on its own: a failed frame of one doesn't cost the others theirs
        foreach (X11Window window in _windows.Values.ToList())
            App.RunGuarded(window.PaintIfRequested);
    }

    private void WaitForEventOrTimeout()
    {
        // there are events already — there is nothing to wait for
        if (X11.XPending(Display) > 0)
            return;

        // a frame is waiting — waiting for input would delay it by up to 100 ms
        foreach (X11Window window in _windows.Values)
            if (window.IsPaintRequested)
                return;

        int fd = X11.XConnectionNumber(Display);

        var readSet = new X11.FdSet();
        readSet.Clear();
        readSet.Set(fd);

        // without animations we wait for an event as long as needed, with animations —
        // we wake up for the next frame, even if there was no input
        int timeoutMs = _tickingWindows.Count > 0 ? _tickIntervalMs : 100;

        var timeout = new X11.TimeVal
        {
            Seconds = timeoutMs / 1000,
            Microseconds = (timeoutMs % 1000) * 1000,
        };

        // the session bus is waited on together with the X connection:
        // a theme change on the desktop wakes the loop like any input
        int nfds = WatchAppearance(ref readSet, fd + 1);

        X11.select(nfds, ref readSet, 0, 0, ref timeout);
    }

    private void DispatchTick()
    {
        if (_tickingWindows.Count == 0)
            return;

        long now = Environment.TickCount64;
        if (now - _lastTickTicks < _tickIntervalMs)
            return;

        _lastTickTicks = now;

        // Tick may stop animations and remove the window from the set —
        // so we go over a copy. Each window on its own, as with painting
        foreach (X11Window window in _tickingWindows.ToList())
            App.RunGuarded(window.RaiseTick);
    }

    public void Exit()
    {
        _running = false;

        foreach (var window in _windows.Values.ToList())
            window.Close();
    }

    internal void WakeUp(X11Window window)
    {
        // wake XNextEvent up by sending the window a message of its own
        var message = new X11.XClientMessageEvent
        {
            type = X11.ClientMessage,
            display = Display,
            window = window.Handle,
            message_type = window.InvokeAtom,
            format = 32,
        };

        nint buffer = Marshal.AllocHGlobal(192);

        try
        {
            Marshal.StructureToPtr(message, buffer, false);
            X11.XSendEvent(Display, window.Handle, false, 0, buffer);
            X11.XFlush(Display);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void Dispatch(nint eventPtr)
    {
        int type = Marshal.ReadInt32(eventPtr);

        switch (type)
        {
            case X11.ButtonPress:
                {
                    var button = Marshal.PtrToStructure<X11.XButtonEvent>(eventPtr);
                    if (!_windows.TryGetValue(button.window, out X11Window? window)) break;

                    // X11 can't "disable" a window the way EnableWindow does in Win32:
                    // modality has to be done by dropping input
                    if (!window.IsInputEnabled) break;

                    var point = new Point(button.x / window.Scale, button.y / window.Scale);
                    KeyModifiers modifiers = ToModifiers(button.state);
                    // a press on the form's picture while a native control types
                    if (button.button is >= 1 and <= 3)
                        window.TakeKeyboardBack();

                    switch (button.button)
                    {
                        case 1: window.Form.OnPointerDown(point, MouseButton.Left, modifiers); break;
                        case 2: window.Form.OnPointerDown(point, MouseButton.Middle, modifiers); break;
                        case 3: window.Form.OnPointerDown(point, MouseButton.Right, modifiers); break;
                        case 4: window.Form.OnMouseWheel(point, 120); break;
                        case 5: window.Form.OnMouseWheel(point, -120); break;
                        // X11 gives the horizontal wheel as button presses
                        case 6: window.Form.OnMouseWheel(point, 0, -120); break;
                        case 7: window.Form.OnMouseWheel(point, 0, 120); break;
                    }

                    break;
                }

            case X11.ButtonRelease:
                {
                    var button = Marshal.PtrToStructure<X11.XButtonEvent>(eventPtr);
                    if (!_windows.TryGetValue(button.window, out X11Window? window)) break;

                    var point = new Point(button.x / window.Scale, button.y / window.Scale);
                    KeyModifiers modifiers = ToModifiers(button.state);

                    switch (button.button)
                    {
                        case 1:
                            window.Form.OnPointerUp(point, MouseButton.Left, modifiers);
                            break;

                        case 2:
                            window.Form.OnPointerUp(point, MouseButton.Middle, modifiers);
                            break;

                        case 3:
                            window.Form.OnPointerUp(point, MouseButton.Right, modifiers);
                            window.Form.OnContextMenu(point);
                            break;
                    }

                    break;
                }

            case X11.KeyRelease:
                {
                    var key = Marshal.PtrToStructure<X11.XKeyEvent>(eventPtr);

                    // the key is up whichever window gets the release
                    OnKeyReleased(key.keycode);

                    if (!_windows.TryGetValue(key.window, out X11Window? window)) break;

                    nuint keysym = X11.XLookupKeysym(eventPtr, 0);
                    window.Form.OnKeyUp(X11KeyMap.ToKey(keysym), ToModifiers(key.state));

                    break;
                }
            case X11.Expose:
                {
                    // XAnyEvent, not XConfigureEvent: the latter has an extra field
                    // before "window", and reading Expose through it never found the window
                    var expose = Marshal.PtrToStructure<X11.XAnyEvent>(eventPtr);

                    // Expose comes in series — one per uncovered rectangle; they all
                    // merge into one deferred frame
                    if (_windows.TryGetValue(expose.window, out X11Window? window))
                        window.Invalidate(null);
                    break;
                }

            case X11.ConfigureNotify:
                {
                    var configure = Marshal.PtrToStructure<X11.XConfigureEvent>(eventPtr);
                    if (_windows.TryGetValue(configure.window, out X11Window? window))
                        window.HandleConfigure(configure.width, configure.height);
                    break;
                }

            case X11.MotionNotify:
                {
                    var motion = Marshal.PtrToStructure<X11.XMotionEvent>(eventPtr);

                    // the modifiers come in the event's state, as for ButtonPress
                    if (_windows.TryGetValue(motion.window, out X11Window? window))
                        window.Form.OnPointerMove(
                            new Point(motion.x / window.Scale, motion.y / window.Scale),
                            ToModifiers(motion.state));
                    break;
                }

            case X11.LeaveNotify:
                {
                    var crossing = Marshal.PtrToStructure<X11.XMotionEvent>(eventPtr);
                    if (_windows.TryGetValue(crossing.window, out X11Window? window))
                        window.Form.OnPointerLeaveWindow();
                    break;
                }

            case X11.KeyPress:
                {
                    var key = Marshal.PtrToStructure<X11.XKeyEvent>(eventPtr);
                    if (!_windows.TryGetValue(key.window, out X11Window? window)) break;

                    // modality by dropping input, as for the mouse above: keystrokes
                    // used to reach the owner window under an open modal dialog
                    if (!window.IsInputEnabled) break;

                    var modifiers = KeyModifiers.None;
                    if ((key.state & X11.ShiftMask) != 0) modifiers |= KeyModifiers.Shift;
                    if ((key.state & X11.ControlMask) != 0) modifiers |= KeyModifiers.Control;
                    if ((key.state & X11.Mod1Mask) != 0) modifiers |= KeyModifiers.Alt;

                    // index 0 is the key's first keysym — the Latin one in every usual
                    // layout, so shortcuts like Ctrl+C keep working under Cyrillic
                    nuint keysym = X11.XLookupKeysym(eventPtr, 0);

                    // X11 sends auto-repeat as an ordinary release+press pair; with
                    // detectable auto-repeat the releases go, and a press of a key
                    // already down is a repeat (see X11Platform.Keyboard)
                    bool isRepeat = OnKeyPressed(key.keycode);

                    window.Form.OnKeyDown(X11KeyMap.ToKey(keysym), modifiers, isRepeat);

                    // printable characters with a separate call; surrogate pairs go
                    // char by char, and the text controls assemble them themselves
                    foreach (char c in window.LookupText(eventPtr))
                        if (!char.IsControl(c))
                            window.Form.OnTextInput(c);

                    break;
                }

            case X11.SelectionRequest:
                {
                    var request = Marshal.PtrToStructure<X11.XSelectionRequestEvent>(eventPtr);
                    _clipboard?.HandleSelectionRequest(request);
                    break;
                }

            case X11.SelectionClear:
                {
                    _clipboard?.HandleSelectionClear();
                    break;
                }

            case X11.FocusIn:
                {
                    var focus = Marshal.PtrToStructure<X11.XFocusChangeEvent>(eventPtr);

                    // the keyboard went into a native control: its slot hears it on the way
                    if (_slots.TryGetValue(focus.window, out X11NativeSlot? slot))
                    {
                        slot.OnFocusIn();
                        break;
                    }

                    if (_windows.TryGetValue(focus.window, out X11Window? window))
                    {
                        window.OnFocusIn();
                        window.Form.OnWindowActivated(true);
                    }

                    break;
                }

            case X11.FocusOut:
                {
                    // we won't learn about key releases anymore — the form resets the
                    // keyboard state, as WM_KILLFOCUS does on Windows. This case used to
                    // be empty, and after Alt+Tab the modifiers stuck
                    var focus = Marshal.PtrToStructure<X11.XFocusChangeEvent>(eventPtr);

                    ForgetHeldKeys();

                    // the keyboard went to a window inside the form's — a native control:
                    // the window is still the active one, only its own focus moved
                    if (focus.detail == X11.NotifyInferior && _windows.TryGetValue(focus.window, out X11Window? owner))
                    {
                        owner.OnFocusOut();
                        owner.Form.OnWindowFocusLost();
                        break;
                    }

                    if (_windows.TryGetValue(focus.window, out X11Window? window))
                    {
                        window.OnFocusOut();
                        window.Form.OnWindowFocusLost();
                        window.Form.OnWindowActivated(false);
                    }

                    break;
                }

            case X11.SelectionNotify:
                {
                    var selection = Marshal.PtrToStructure<X11.XSelectionEvent>(eventPtr);
                    if (!_windows.TryGetValue(selection.requestor, out X11Window? window)) break;

                    // XDND takes only its own transfer property; the clipboard waits
                    // for its event with its own loop and doesn't come here
                    window.DropTarget?.HandleSelection(selection.property);

                    break;
                }

            case X11.ClientMessage:
                {
                    var message = Marshal.PtrToStructure<X11.XClientMessageEvent>(eventPtr);
                    if (!_windows.TryGetValue(message.window, out X11Window? window)) break;

                    // XDND is checked first: its messages are the most numerous,
                    // and they don't overlap with either the invoke queue or closing
                    if (window.DropTarget?.Handle(message) == true)
                        break;

                    if (message.message_type == window.InvokeAtom)
                    {
                        window.DrainInvokeQueue();
                    }
                    else if (window.IsDeleteMessage((nuint)message.data0))
                    {
                        // the close button of the title bar: the form's Closing
                        // may keep the window open
                        if (window.Form.RequestClose(CloseReason.UserClosing))
                            window.Close();
                    }

                    break;
                }
        }
    }

    private static KeyModifiers ToModifiers(uint state)
    {
        var modifiers = KeyModifiers.None;

        if ((state & X11.ShiftMask) != 0) modifiers |= KeyModifiers.Shift;
        if ((state & X11.ControlMask) != 0) modifiers |= KeyModifiers.Control;
        if ((state & X11.Mod1Mask) != 0) modifiers |= KeyModifiers.Alt;

        return modifiers;
    }
}