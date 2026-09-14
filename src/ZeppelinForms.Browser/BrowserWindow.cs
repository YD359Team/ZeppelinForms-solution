using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Browser;

/// <summary>
/// Форма как слой на общем canvas. Поверхностью и раздачей ввода владеет
/// платформа — окно знает только своё место на холсте. IDesktopWindow
/// не реализует намеренно: заголовка, прозрачности и состояния окна
/// в браузере нет, и Form сам пропустит эти вызовы, увидев null.
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

    /// <summary>Левый верхний угол формы на холсте в логических единицах.
    /// У нижней формы всегда ноль, у диалогов ставится платформой.</summary>
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

    /// <summary>Область игнорируется: кадр всё равно уходит на canvas целиком,
    /// да и слои поверх пришлось бы перерисовывать вместе с ним. Сама
    /// отрисовка откладывается до ближайшего кадра — см. BrowserPlatform.Invalidate.</summary>
    public void Invalidate(Rectangle? rect) => _platform.Invalidate();

    public void Invoke(Action action) => _platform.Enqueue(action);

    internal void HandleFrame(double timestampMs)
    {
        if (!_frames.ShouldTick(timestampMs)) return;

        _form.Tick();
    }

    // ==== ввод ====
    // Точки приходят в координатах холста; форма ждёт свои, поэтому
    // из каждой вычитается Origin. У нижней формы он нулевой.

    private Point ToLocal(double x, double y) =>
        new((float)x - Origin.X, (float)y - Origin.Y);

    /// <summary>Идентификаторы касаний браузера произвольны и могут совпасть
    /// с Form.MousePointerId. Раздаём свои, начиная с десяти, и держим
    /// соответствие, пока контакт жив.</summary>
    private readonly Dictionary<int, int> _pointerIds = [];
    private int _nextPointerId = 10;

    /// <summary>Сдвиг между временем браузера (от начала загрузки страницы)
    /// и Environment.TickCount64, которым живёт Form. Берётся по первому
    /// событию: сравнивать между собой можно только однородные величины,
    /// а длительность удержания считается именно вычитанием.</summary>
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

        // отпущенный палец больше не вернётся под этим идентификатором,
        // иначе словарь рос бы всю жизнь страницы
        ForgetPointer(pointerId);
    }

    internal void HandlePointerCancel(int pointerId)
    {
        if (!_pointerIds.TryGetValue(pointerId, out int mapped)) return;

        _form.OnPointerCancel(mapped);
        ForgetPointer(pointerId);
    }

    internal void HandlePointerLeave()
    {
        // при захвате указателя браузер продолжает присылать события
        // за пределами canvas — уход курсора тогда не считается уходом
        if (_captured) return;

        _form.OnPointerLeaveWindow();
    }

    internal void HandleWheel(double x, double y, double deltaY, double deltaX) =>
        // в Win32 положительная дельта — прокрутка вверх, в браузере наоборот
        _form.OnMouseWheel(ToLocal(x, y), -(int)deltaY, -(int)deltaX);

    internal void HandleContextMenu(double x, double y) => _form.OnContextMenu(ToLocal(x, y));

    internal void HandleKeyDown(string code, string key, int modifiers, bool isRepeat)
    {
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

    public void SetEnabled(bool enabled) => IsInputEnabled = enabled;

    /// <summary>Поднять слой наверх. Так диалог оказывается над владельцем
    /// независимо от того, в каком порядке их создали.</summary>
    public void Activate() => _platform.BringToFront(this);
}