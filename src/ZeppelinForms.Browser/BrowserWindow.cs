using System.Collections.Concurrent;
using SkiaSharp;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Browser;

/// <summary>
/// Форма поверх одного canvas. IDesktopWindow не реализует намеренно:
/// заголовка, прозрачности и состояния окна в браузере нет, и Form
/// сам пропустит эти вызовы, увидев null.
/// </summary>
internal sealed class BrowserWindow : IPlatformWindow
{
    private readonly Form _form;
    private readonly BrowserSkiaSurface _surface = new();
    private readonly BrowserFrameDriver _frames;
    private readonly ConcurrentQueue<Action> _invokeQueue = new();

    private float _scale = 1f;
    private bool _inputEnabled = true;
    private bool _captured;
    private bool _closed;

    public BrowserWindow(Form form)
    {
        _form = form;
        _frames = new BrowserFrameDriver(Interop.RequestFrame, Paint);
    }

    public IFrameDriver Frames => _frames;

    public float Scale => _scale;

    public void Show() => Invalidate(null);

    public void Close()
    {
        if (_closed) return;
        _closed = true;

        _frames.Stop();
        _surface.Dispose();

        _form.OnWindowClosed();
    }

    /// <summary>Область игнорируется: кадр всё равно уходит на canvas целиком.
    /// Перерисовка немедленная — отложить её нечем, отдельного цикла событий
    /// у нас нет, а rAF пришлось бы ждать до следующего кадра.</summary>
    public void Invalidate(Rectangle? rect) => Paint();

    private void Paint()
    {
        if (_closed) return;

        if (_surface.BeginFrame() is SKSurface skSurface)
        {
            Skia.SkiaRenderer.Render(_form, skSurface.Canvas, _scale);
            _surface.EndFrame();
        }

        _form.TakeDirtyRegion();
    }

    public void Invoke(Action action)
    {
        _invokeQueue.Enqueue(action);
        Interop.ScheduleDrain();
    }

    internal void DrainInvokes()
    {
        while (_invokeQueue.TryDequeue(out Action? action))
            action();
    }

    // ==== кадры ====

    internal void HandleFrame(double timestampMs)
    {
        if (!_frames.ShouldTick(timestampMs)) return;

        _form.Tick();
    }

    internal void HandleResize(int physicalWidth, int physicalHeight, float scale)
    {
        _scale = scale <= 0 ? 1f : scale;

        _surface.Resize(physicalWidth, physicalHeight);
        _form.ClientSize = new Size(physicalWidth / _scale, physicalHeight / _scale);
        _form.PerformLayout();

        Paint();
    }

    // ==== ввод ====
    // Координаты из JS приходят в CSS-пикселях, то есть уже логические:
    // масштаб заложен в размер canvas, а не в события мыши.

    internal void HandlePointerMove(double x, double y, int modifiers)
    {
        if (!_inputEnabled) return;

        _form.OnPointerMove(new Point((float)x, (float)y), (KeyModifiers)modifiers);
    }

    internal void HandlePointerDown(double x, double y, int button, int modifiers)
    {
        if (!_inputEnabled) return;

        _form.OnPointerDown(new Point((float)x, (float)y), ToButton(button), (KeyModifiers)modifiers);
    }

    internal void HandlePointerUp(double x, double y, int button, int modifiers)
    {
        if (!_inputEnabled) return;

        _form.OnPointerUp(new Point((float)x, (float)y), ToButton(button), (KeyModifiers)modifiers);
    }

    internal void HandlePointerLeave()
    {
        // при захвате указателя браузер продолжает присылать события
        // за пределами canvas — уход курсора тогда не считается уходом
        if (!_inputEnabled || _captured) return;

        _form.OnPointerLeaveWindow();
    }

    internal void HandleWheel(double x, double y, double deltaY, double deltaX)
    {
        if (!_inputEnabled) return;

        // в Win32 положительная дельта — прокрутка вверх, в браузере наоборот
        _form.OnMouseWheel(new Point((float)x, (float)y), -(int)deltaY, -(int)deltaX);
    }

    internal void HandleContextMenu(double x, double y)
    {
        if (!_inputEnabled) return;

        _form.OnContextMenu(new Point((float)x, (float)y));
    }

    internal void HandleKeyDown(string code, string key, int modifiers, bool isRepeat)
    {
        if (!_inputEnabled) return;

        var mods = (KeyModifiers)modifiers;
        _form.OnKeyDown(BrowserKeyMap.FromCode(code), mods, isRepeat);

        // текст не отдаём, когда нажат Control или Alt: это сочетание,
        // а не ввод. Shift при этом ввод не отменяет
        if ((mods & (KeyModifiers.Control | KeyModifiers.Alt)) != 0) return;

        if (BrowserKeyMap.ToTextInput(key) is { } c)
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

    // ==== остальное из контракта ====

    /// <summary>Захвата как в Win32 нет: setPointerCapture ставится в JS
    /// на pointerdown безусловно, потому что без него браузер обрывает
    /// перетаскивание на выходе за canvas. Здесь только отметка,
    /// чтобы не считать уход курсора уходом из окна.</summary>
    public void CaptureMouse() => _captured = true;

    public void ReleaseMouseCapture() => _captured = false;

    public void SetCursor(CursorKind cursor) => Interop.SetCursor(Interop.ToCssCursor(cursor));

    /// <summary>Перетаскивание файлов из системы пока не поддержано:
    /// HTML5 drag-and-drop — отдельная работа, а не переключатель.</summary>
    public void SetDragDropEnabled(bool enabled) { }

    public void SetEnabled(bool enabled) => _inputEnabled = enabled;

    /// <summary>Окно ровно одно и оно всегда активно.</summary>
    public void Activate() { }

    internal Form Form => _form;
}