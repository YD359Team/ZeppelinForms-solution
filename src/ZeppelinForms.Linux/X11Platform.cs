using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Linux;

public sealed class X11Platform : IPlatform, INestedLoopSupport
{
    private readonly Dictionary<nuint, X11Window> _windows = [];
    private bool _running;

    internal nint Display { get; private set; }

    public X11Platform()
    {
        Display = X11.XOpenDisplay(0);

        if (Display == 0)
            throw new InvalidOperationException(
                "Could not connect to the X server. Check the DISPLAY variable.");

        Skia.SkiaImageDecoder.Register();
        Skia.SkiaTextMeasurer.Register();
        Skia.SkiaOffscreenRenderer.Register();
    }

    private X11Clipboard? _clipboard;

    internal void Register(X11Window window)
    {
        _windows[window.Handle] = window;

        // the clipboard needs an owner window, so it is created
        // not in the platform's constructor but together with the first window
        if (_clipboard is null)
        {
            _clipboard = new X11Clipboard(Display, window.Handle);
            Clipboard.Current = _clipboard;
        }
    }

    internal void Unregister(X11Window window)
    {
        _windows.Remove(window.Handle);

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
                Dispatch(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        DispatchTick();

        // one frame per pump for every window that asked for one: after the input
        // and the tick, so that everything they changed goes into the same frame.
        // A copy — painting a window must not trip over a window closing
        foreach (X11Window window in _windows.Values.ToList())
            window.PaintIfRequested();
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

        X11.select(fd + 1, ref readSet, 0, 0, ref timeout);
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
        // so we go over a copy
        foreach (X11Window window in _tickingWindows.ToList())
            window.RaiseTick();
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
                    if (_windows.TryGetValue(motion.window, out X11Window? window))
                        window.Form.OnPointerMove(new Point(motion.x / window.Scale, motion.y / window.Scale));
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

                    nuint keysym = X11.XLookupKeysym(eventPtr, 0);
                    // X11 sends auto-repeat as an ordinary release+press pair;
                    // they can be told apart only through XkbSetDetectableAutoRepeat — TODO
                    window.Form.OnKeyDown(X11KeyMap.ToKey(keysym), modifiers, isRepeat: false);

                    // printable characters with a separate call
                    byte[] buffer = new byte[8];
                    int count = X11.XLookupString(eventPtr, buffer, buffer.Length, out _, 0);

                    for (int i = 0; i < count; i++)
                    {
                        char c = (char)buffer[i];
                        if (!char.IsControl(c))
                            window.Form.OnTextInput(c);
                    }

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

            case X11.FocusOut:
                {
                    // we won't learn about key releases anymore — the form resets the
                    // keyboard state, as WM_KILLFOCUS does on Windows. This case used to
                    // be empty, and after Alt+Tab the modifiers stuck
                    var focus = Marshal.PtrToStructure<X11.XAnyEvent>(eventPtr);

                    if (_windows.TryGetValue(focus.window, out X11Window? window))
                        window.Form.OnWindowFocusLost();

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