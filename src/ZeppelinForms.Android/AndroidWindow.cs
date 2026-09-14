using Android.Views;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Android;

/// <summary>
/// Форма как слой на общей поверхности — устройство то же, что в браузере.
/// IDesktopWindow не реализует: заголовка, границ и состояния окна
/// на Android нет.
/// </summary>
internal sealed class AndroidWindow : IPlatformWindow
{
    private readonly AndroidPlatform _platform;
    private readonly Form _form;
    private readonly AndroidFrameDriver _frames;

    private bool _closed;

    public AndroidWindow(AndroidPlatform platform, Form form)
    {
        _platform = platform;
        _form = form;
        _frames = new AndroidFrameDriver(platform.ScheduleFrame, platform.Invalidate);
    }

    internal Form Form => _form;

    /// <summary>Левый верхний угол формы на поверхности в логических единицах.</summary>
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

    // ==== ввод ====

    /// <summary>Идентификаторы Android лежат в 0..9 и стабильны, пока контакт
    /// жив, поэтому словарь соответствий, как в браузере, не нужен —
    /// хватает сдвига, уводящего их от Form.MousePointerId.</summary>
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
            kind == PointerKind.Mouse ? Form.MousePointerId : TouchIdBase + pointerId,
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

    internal void HandleTouchCancel(int pointerId) =>
        _form.OnPointerCancel(TouchIdBase + pointerId);

    // ==== остальное из контракта ====

    /// <summary>Захват на Android не нужен: касание и так доставляется
    /// тому View, который принял ACTION_DOWN, вплоть до отпускания.</summary>
    public void CaptureMouse() { }

    public void ReleaseMouseCapture() { }

    /// <summary>Курсора нет.</summary>
    public void SetCursor(CursorKind cursor) { }

    /// <summary>Перетаскивание из других приложений не поддержано.</summary>
    public void SetDragDropEnabled(bool enabled) { }

    public void SetEnabled(bool enabled) => IsInputEnabled = enabled;

    public void Activate() => _platform.BringToFront(this);
}