using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Browser;

/// <summary>
/// A form as a layer on the shared canvas. The platform owns the surface and input
/// distribution — the window knows only its place on the canvas. It deliberately
/// doesn't implement IDesktopWindow: a browser has no title bar, transparency or
/// window state, and Form skips those calls by itself when it sees null.
/// </summary>
internal sealed class BrowserWindow : IPlatformWindow
{
    private readonly BrowserPlatform _platform;
    private readonly Form _form;
    private readonly BrowserFrameDriver _frames;

    private bool _captured;
    private bool _closed;

    public BrowserWindow(BrowserPlatform platform, Form form)
    {
        _platform = platform;
        _form = form;
        _frames = new BrowserFrameDriver(Interop.RequestFrame, platform.Invalidate);
    }

    internal Form Form => _form;

    /// <summary>The form's top-left corner on the canvas in logical units.
    /// Always zero for the bottom form, set by the platform for dialogs.</summary>
    internal Point Origin { get; set; }

    internal bool IsInputEnabled { get; private set; } = true;

    public IFrameDriver Frames => _frames;

    public float Scale => _platform.Scale;

    public void Show() => _platform.Invalidate();

    public void Close()
    {
        if (_closed) return;
        _closed = true;

        _frames.Stop();
        _platform.Remove(this);

        _form.OnWindowClosed();
    }

    /// <summary>The area is ignored: the frame goes onto the canvas whole anyway,
    /// and the layers above would have to be redrawn with it. The drawing itself
    /// is deferred until the next frame — see BrowserPlatform.Invalidate.</summary>
    public void Invalidate(Rectangle? rect) => _platform.Invalidate();

    public void Invoke(Action action) => _platform.Enqueue(action);

    internal void HandleFrame(double timestampMs)
    {
        if (!_frames.ShouldTick(timestampMs)) return;

        _form.Tick();
    }

    // ==== input ====
    // Points come in canvas coordinates; the form expects its own, so Origin
    // is subtracted from each. For the bottom form it is zero.

    private Point ToLocal(double x, double y) =>
        new((float)x - Origin.X, (float)y - Origin.Y);

    /// <summary>Browser touch identifiers are arbitrary and may coincide with
    /// Form.MousePointerId. We hand out our own, starting from ten, and keep
    /// the mapping while the contact is alive.</summary>
    private readonly Dictionary<int, int> _pointerIds = [];
    private int _nextPointerId = 10;

    /// <summary>The offset between browser time (since the page started loading)
    /// and Environment.TickCount64, which Form lives by. Taken from the first event:
    /// only homogeneous values can be compared, and the hold duration is computed
    /// exactly by subtraction.</summary>
    private long? _timeOffset;

    private long ToTicks(double timestampMs)
    {
        _timeOffset ??= Environment.TickCount64 - (long)timestampMs;
        return _timeOffset.Value + (long)timestampMs;
    }

    private static PointerKind ToKind(int kind) => kind switch
    {
        1 => PointerKind.Touch,
        2 => PointerKind.Pen,
        _ => PointerKind.Mouse,
    };

    private int MapPointerId(int browserId, PointerKind kind)
    {
        if (kind == PointerKind.Mouse) return Form.MousePointerId;

        if (_pointerIds.TryGetValue(browserId, out int mapped)) return mapped;

        mapped = _nextPointerId++;
        _pointerIds[browserId] = mapped;

        return mapped;
    }

    private void ForgetPointer(int browserId) => _pointerIds.Remove(browserId);

    private PointerEventArgs ToArgs(
        double x, double y, int browserId, int kind, int button, double pressure, double timestampMs, int modifiers)
    {
        PointerKind pointerKind = ToKind(kind);

        return new PointerEventArgs(
            MapPointerId(browserId, pointerKind),
            pointerKind,
            ToLocal(x, y),
            ToButton(button),
            (float)pressure,
            (KeyModifiers)modifiers)
        {
            Timestamp = ToTicks(timestampMs),
        };
    }

    internal void HandlePointerMove(
        double x, double y, int pointerId, int kind, double pressure, double timestampMs, int modifiers) =>
        _form.OnPointerMove(ToArgs(x, y, pointerId, kind, 0, pressure, timestampMs, modifiers));

    internal void HandlePointerDown(
        double x, double y, int pointerId, int kind, int button, double pressure, double timestampMs, int modifiers) =>
        _form.OnPointerDown(ToArgs(x, y, pointerId, kind, button, pressure, timestampMs, modifiers));

    internal void HandlePointerUp(
        double x, double y, int pointerId, int kind, int button, double pressure, double timestampMs, int modifiers)
    {
        _form.OnPointerUp(ToArgs(x, y, pointerId, kind, button, pressure, timestampMs, modifiers));

        // a released finger won't come back under this identifier,
        // otherwise the dictionary would grow for the whole life of the page
        ForgetPointer(pointerId);
    }

    internal void HandlePointerCancel(int pointerId)
    {
        if (!_pointerIds.TryGetValue(pointerId, out int mapped))
        {
            // the mouse doesn't get into the dictionary: its identifier is fixed
            _form.OnPointerCancel(Form.MousePointerId);
            return;
        }

        _form.OnPointerCancel(mapped);
        ForgetPointer(pointerId);
    }

    internal void HandlePointerLeave()
    {
        // with pointer capture the browser keeps sending events beyond
        // the canvas — the cursor leaving then doesn't count as leaving
        if (_captured) return;

        _form.OnPointerLeaveWindow();
    }

    internal void HandleWheel(double x, double y, double deltaY, double deltaX) =>
        // in Win32 a positive delta scrolls up, in a browser it's the opposite
        _form.OnMouseWheel(ToLocal(x, y), -(int)deltaY, -(int)deltaX);

    internal void HandleContextMenu(double x, double y) => _form.OnContextMenu(ToLocal(x, y));

    internal void HandleKeyDown(string code, string key, int modifiers, bool isRepeat)
    {
        var mods = (KeyModifiers)modifiers;
        _form.OnKeyDown(BrowserKeyMap.FromCode(code), mods, isRepeat);

        // Control or Alt alone makes a shortcut, not input. Both together are AltGr
        // on European layouts — browsers on Windows report it as Ctrl+Alt — and that
        // is how @, € and [ are typed on a German keyboard. The old check dropped text
        // whenever either was down, and those characters could not be typed at all
        bool control = (mods & KeyModifiers.Control) != 0;
        bool alt = (mods & KeyModifiers.Alt) != 0;

        if (control != alt) return;

        // one visible character may be two chars (an emoji, a letter beyond the BMP):
        // they go one by one, and the text controls assemble the pair themselves
        if (BrowserKeyMap.ToTextInput(key) is { } text)
            foreach (char c in text)
                _form.OnTextInput(c);
    }

    internal void HandleKeyUp(string code, int modifiers) =>
        _form.OnKeyUp(BrowserKeyMap.FromCode(code), (KeyModifiers)modifiers);

    internal void HandleFocusLost() => _form.OnWindowFocusLost();

    private static MouseButton ToButton(int button) => button switch
    {
        1 => MouseButton.Middle,
        2 => MouseButton.Right,
        _ => MouseButton.Left,
    };

    // ==== the rest of the contract ====

    /// <summary>There is no capture as in Win32: setPointerCapture is set in JS
    /// on pointerdown unconditionally, because without it the browser breaks off
    /// the drag at the canvas edge. Here it is only a mark, so that the cursor
    /// leaving isn't counted as leaving the window.</summary>
    public void CaptureMouse() => _captured = true;

    public void ReleaseMouseCapture() => _captured = false;

    public void SetCursor(CursorKind cursor) => Interop.SetCursor(Interop.ToCssCursor(cursor));

    /// <summary>Dragging files from the system is not supported yet:
    /// HTML5 drag-and-drop is separate work, not a switch.</summary>
    public void SetDragDropEnabled(bool enabled) { }

    public void SetEnabled(bool enabled) => IsInputEnabled = enabled;

    /// <summary>Raise the layer to the top. This way the dialog ends up above its owner
    /// regardless of the order they were created in.</summary>
    public void Activate() => _platform.BringToFront(this);
}