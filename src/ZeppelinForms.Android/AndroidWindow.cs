using Android.Views;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Android;

/// <summary>
/// A form as a layer on the shared surface — the same arrangement as in the browser.
/// It doesn't implement IDesktopWindow: Android has no title bar, frame or
/// window state.
/// </summary>
internal sealed class AndroidWindow : IPlatformWindow, ISoftKeyboard
{
    private readonly AndroidPlatform _platform;
    private readonly Form _form;
    private readonly AndroidFrameDriver _frames;

    private bool _closed;

    // ==== the on-screen keyboard ====

    public void ShowSoftKeyboard(SoftKeyboardKind kind) => _platform.ShowSoftKeyboard(kind);

    public void HideSoftKeyboard() => _platform.HideSoftKeyboard();

    public AndroidWindow(AndroidPlatform platform, Form form)
    {
        _platform = platform;
        _form = form;
        _frames = new AndroidFrameDriver(platform.ScheduleFrame, platform.Invalidate);
    }

    internal Form Form => _form;

    /// <summary>The form's top-left corner on the surface in logical units.</summary>
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

    public void Invalidate(Rectangle? rect) => _platform.Invalidate();

    public void Invoke(Action action) => _platform.Post(action);

    internal void HandleFrame(double timestampMs)
    {
        if (!_frames.ShouldTick(timestampMs)) return;

        _form.Tick();
    }

    // ==== input ====

    /// <summary>Android identifiers lie in 0..9 and are stable while the contact is
    /// alive, so a mapping dictionary, as in the browser, is not needed — a shift
    /// taking them away from Form.MousePointerId is enough.</summary>
    private const int TouchIdBase = 10;

    private Point ToLocal(float x, float y) => new(x - Origin.X, y - Origin.Y);

    private static PointerKind ToKind(MotionEventToolType toolType) => toolType switch
    {
        MotionEventToolType.Stylus or MotionEventToolType.Eraser => PointerKind.Pen,
        MotionEventToolType.Mouse => PointerKind.Mouse,
        _ => PointerKind.Touch,
    };

    private PointerEventArgs ToArgs(
        float x, float y, int pointerId, MotionEventToolType toolType, float pressure, long timestamp)
    {
        PointerKind kind = ToKind(toolType);

        return new PointerEventArgs(
            MapPointerId(pointerId, kind),
            kind,
            ToLocal(x, y),
            MouseButton.Left,
            pressure)
        {
            Timestamp = timestamp,
        };
    }

    internal void HandleTouchDown(
        float x, float y, int pointerId, MotionEventToolType toolType, float pressure, long timestamp) =>
        _form.OnPointerDown(ToArgs(x, y, pointerId, toolType, pressure, timestamp));

    internal void HandleTouchMove(
        float x, float y, int pointerId, MotionEventToolType toolType, float pressure, long timestamp) =>
        _form.OnPointerMove(ToArgs(x, y, pointerId, toolType, pressure, timestamp));

    internal void HandleTouchUp(
        float x, float y, int pointerId, MotionEventToolType toolType, float pressure, long timestamp) =>
        _form.OnPointerUp(ToArgs(x, y, pointerId, toolType, pressure, timestamp));

    /// <summary>The only place where the contact identifier is decided.
    /// This rule must not be split in two: a cancel has to reach the same
    /// contact as the press.</summary>
    private static int MapPointerId(int pointerId, PointerKind kind) =>
        kind == PointerKind.Mouse ? Form.MousePointerId : TouchIdBase + pointerId;

    internal void HandleTouchCancel(int pointerId, MotionEventToolType toolType) =>
        _form.OnPointerCancel(MapPointerId(pointerId, ToKind(toolType)));

    /// <summary>The wheel of a connected mouse.</summary>
    internal void HandleWheel(float x, float y, int delta, int horizontalDelta) =>
        _form.OnMouseWheel(ToLocal(x, y), delta, horizontalDelta);

    /// <summary>The cursor moved without a press. Only for the mouse: touch has no
    /// "over the element" state, and it must not be synthesized — otherwise the
    /// highlight would stick after the finger is released.</summary>
    internal void HandleHoverMove(float x, float y, long timestamp) =>
        _form.OnPointerMove(new PointerEventArgs(
            Form.MousePointerId,
            PointerKind.Mouse,
            ToLocal(x, y),
            MouseButton.Left,
            0f)
        {
            Timestamp = timestamp,
        });

    /// <summary>The cursor left the window.</summary>
    internal void HandlePointerLeave() => _form.OnPointerLeaveWindow();

    // ==== the rest of the contract ====

    /// <summary>No capture is needed on Android: a touch is delivered to the View
    /// that accepted ACTION_DOWN anyway, right up to the release.</summary>
    public void CaptureMouse() { }

    public void ReleaseMouseCapture() { }

    /// <summary>There is no cursor.</summary>
    public void SetCursor(CursorKind cursor) { }

    /// <summary>Dragging from other applications is not supported.</summary>
    public void SetDragDropEnabled(bool enabled) { }

    public void SetEnabled(bool enabled) => IsInputEnabled = enabled;

    public void Activate() => _platform.BringToFront(this);
}