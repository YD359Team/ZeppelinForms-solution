using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Linux;

public sealed class X11Platform : IPlatform
{
    private readonly Dictionary<nuint, X11Window> _windows = [];
    private bool _running;

    internal nint Display { get; private set; }

    public X11Platform()
    {
        Display = X11.XOpenDisplay(0);

        if (Display == 0)
            throw new InvalidOperationException(
                "Не удалось подключиться к X-серверу. Проверьте переменную DISPLAY.");

        Skia.SkiaImageDecoder.Register();
        Skia.SkiaTextMeasurer.Register();
        Skia.SkiaElementRenderer.Register();
    }

    private X11Clipboard? _clipboard;

    internal void Register(X11Window window)
    {
        _windows[window.Handle] = window;

        // буферу обмена нужно окно-владелец, поэтому создаём его
        // не в конструкторе платформы, а вместе с первым окном
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

    public void RunModal(IPlatformWindow dialog, IPlatformWindow? owner)
    {
        var dialogWindow = (X11Window)dialog;

        // вложенный цикл: крутится, пока живо окно диалога
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

    public void Run()
    {
        _running = true;

        while (_running)
            PumpOnce();
    }

    private void PumpOnce()
    {
        WaitForEventOrTimeout();

        // разбираем всё, что накопилось, не блокируясь
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
    }

    private void WaitForEventOrTimeout()
    {
        // события уже есть — ждать нечего
        if (X11.XPending(Display) > 0)
            return;

        int fd = X11.XConnectionNumber(Display);

        var readSet = new X11.FdSet();
        readSet.Clear();
        readSet.Set(fd);

        // без анимаций ждём событие сколь угодно долго, с анимациями —
        // просыпаемся к следующему кадру, даже если ввода не было
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

        // Tick может остановить анимации и убрать окно из набора —
        // поэтому идём по копии
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
        // будим XNextEvent, отправив окну собственное сообщение
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
            case X11.Expose:
                {
                    var configure = Marshal.PtrToStructure<X11.XConfigureEvent>(eventPtr);
                    if (_windows.TryGetValue(configure.window, out X11Window? window))
                        window.Paint();
                    break;
                }

            case X11.ConfigureNotify:
                {
                    var configure = Marshal.PtrToStructure<X11.XConfigureEvent>(eventPtr);
                    if (_windows.TryGetValue(configure.window, out X11Window? window))
                        window.HandleConfigure(configure.width, configure.height);
                    break;
                }

            case X11.ButtonPress:
                {
                    var button = Marshal.PtrToStructure<X11.XButtonEvent>(eventPtr);
                    if (!_windows.TryGetValue(button.window, out X11Window? window)) break;

                    var point = new Point(button.x / window.Scale, button.y / window.Scale);

                    // 4 и 5 — это прокрутка колеса, а не кнопки
                    if (button.button == 4) window.Form.OnMouseWheel(point, 120);
                    else if (button.button == 5) window.Form.OnMouseWheel(point, -120);
                    else if (button.button == 1) window.Form.OnPointerDown(point);
                    break;
                }

            case X11.ButtonRelease:
                {
                    var button = Marshal.PtrToStructure<X11.XButtonEvent>(eventPtr);
                    if (!_windows.TryGetValue(button.window, out X11Window? window)) break;

                    if (button.button == 1)
                        window.Form.OnPointerUp(new Point(button.x / window.Scale, button.y / window.Scale));
                    else if (button.button == 3)
                        window.Form.OnContextMenu(new Point(button.x / window.Scale, button.y / window.Scale));
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

                    var modifiers = KeyModifiers.None;
                    if ((key.state & X11.ShiftMask) != 0) modifiers |= KeyModifiers.Shift;
                    if ((key.state & X11.ControlMask) != 0) modifiers |= KeyModifiers.Control;
                    if ((key.state & X11.Mod1Mask) != 0) modifiers |= KeyModifiers.Alt;

                    nuint keysym = X11.XLookupKeysym(eventPtr, 0);
                    window.Form.OnKeyDown(X11KeyMap.ToKey(keysym), modifiers);

                    // печатные символы отдельным вызовом
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

            case X11.ClientMessage:
                {
                    var message = Marshal.PtrToStructure<X11.XClientMessageEvent>(eventPtr);
                    if (!_windows.TryGetValue(message.window, out X11Window? window)) break;

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
        }
    }
}

