using System.Diagnostics;
using ZeppelinForms.Animation;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Controls.Tools;
using ZeppelinForms.Forms.Dispatchers;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Input.DragDrop;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Forms;

public class Form : IDisposable
{
    /// <summary>Флаут закрыт — по клику мимо, программно или вместе с формой.
    /// Контролы, открывшие его, обязаны сбросить свою ссылку здесь.</summary>
    public event EventHandler<UIElement>? FlyoutClosed;
    public event EventHandler? Shown;

    /// <summary>Окно разрушено. Приходит независимо от того, откуда пришло
    /// закрытие: Accept, Cancel, крестик в заголовке или сама система.</summary>
    public event EventHandler? Closed;

    internal IPlatformWindow? PlatformWindow
    {
        get;
        set
        {
            field = value;

            if (value is null) return;

            // новое окно — новая жизнь: форму могли закрыть и показать заново
            _isClosed = false;

            if (!s_openForms.Contains(this))
                s_openForms.Add(this);

            // окно только что появилось: если приём перетаскивания включили
            // до показа, платформа об этом ещё не знает
            if (AllowDrop)
                value.SetDragDropEnabled(true);
        }
    }

    private static readonly List<Form> s_openForms = [];

    /// <summary>Формы с живым окном. Нужен жизненному циклу приложения:
    /// уход в фон касается всех окон, а не только главного.</summary>
    public static IReadOnlyList<Form> OpenForms => s_openForms;

    /// <summary>Окно как объект рабочего стола. null там, где рабочего стола
    /// нет: в браузере и на Android заголовка, прозрачности и состояния
    /// окна не существует, и молча ничего не делать — правильное поведение.</summary>
    internal IDesktopWindow? DesktopWindow => PlatformWindow as IDesktopWindow;

    public WindowStartupLocation WindowStartupLocation { get; set; }

    /// <summary>Принимать ли перетаскивание из системы в это окно.
    /// Без этого AllowDrop у элементов не сработает: окно не зарегистрировано
    /// приёмником, и система о нём не знает.</summary>
    public bool AllowDrop
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            PlatformWindow?.SetDragDropEnabled(value);
        }
    }

    private Point _themeRippleOrigin;
    private float _themeRippleRadius;
    private Color _themeRippleColor;
    private bool _themeRippleActive;

    private long _lastClickTicks;
    private Point _lastClickPoint;
    private int _clickCount;
    private MouseButton _lastClickButton;

    public int DoubleClickIntervalMs { get; set; } = 400;
    public float DoubleClickSlop { get; set; } = 4f;

    private float _opacity = 1f;

    public float Opacity
    {
        get => _opacity;
        set
        {
            _opacity = Math.Clamp(value, 0f, 1f);
            DesktopWindow?.SetOpacity(_opacity);
        }
    }

    public string Name { get; set; } = string.Empty;
    public string? Title { get; set; }
    public Icon? Icon { get; set; }
    public Point Position { get; set; }
    public Size Size { get; set; }
    // TODO: add min\max size support
    public Size MinimumSize { get; set; } = Size.Auto;
    public Size MaximumSize { get; set; } = Size.Auto;

    /// <summary>Форма показана как модальный диалог.</summary>
    public bool IsDialog { get; private set; }

    public Font? Font { get; set; }

    private WindowState _windowState = WindowState.Normal;

    public bool CanMinimize { get; set; } = true;
    public bool CanMaximize { get; set; } = true;
    public bool CanResize { get; set; } = true;

    private readonly List<IAnimation> _animations = [];
    private long _lastTickTicks;

    public int FrameIntervalMs { get; set; } = 16;   // ~60 кадров в секунду

    public WindowState WindowState
    {
        get => _windowState;
        set
        {
            if (_windowState == value) return;

            _windowState = value;
            DesktopWindow?.SetWindowState(value);
            WindowStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? WindowStateChanged;

    public NameScope NameScope { get; } = new();

    public UIElement? Content
    {
        get;
        set
        {
            if (field == value) return;

            if (field is not null)
            {
                DetachTree(field);
                field.Owner = null;
            }

            field = value;

            if (value is not null)
            {
                value.Owner = this;
                AttachTree(value);
            }

            // смена содержимого — это полная смена геометрии,
            // нужен пересчёт раскладки и перерисовка всего окна
            Invalidate();
        }
    }

    public Size ClientSize { get; internal set; }

    public FlowDirection? FlowDirection { get; set; }

    // Список приватный и меняется только через AttachOverlay/DetachOverlay.
    // Раньше он был обычным List, и пять методов из шести клали элемент
    // напрямую — тема до оверлея не доезжала, а при закрытии не звался
    // DetachTree. Прямого Add больше нет, поэтому забыть про присоединение
    // нельзя: единственный путь внутрь проходит через него.
    private readonly List<UIElement> _overlays = [];
    public IReadOnlyList<UIElement> Overlays => _overlays;
    private readonly List<UIElement> _flyouts = [];
    private readonly List<UIElement> _toasts = [];

    private UIElement? _dropTarget;
    private UIElement? _hoveredElement;
    private UIElement? _pressedElement;
    private UIElement? _mouseCapture;
    private CursorKind _lastCursor = CursorKind.Arrow;
    private readonly FocusDispatcher _focusDispatcher = new();

    // ===== ToolTip =====
    public int ToolTipDelay { get; set; } = 700;
    private readonly System.Threading.Timer _toolTipTimer;
    private UIElement? _toolTipOwner;
    private UIElement? _activeToolTip;
    private Point _lastPointerPosition;

    // ===== Инспектор (F12) =====
    public bool IsInspectorEnabled { get; private set; }
    public UIElement? InspectedElement { get; private set; }
    private bool IsInsideInspector(Point point) =>
_inspectorGrid is not null && HitTester.HitTest(_inspectorGrid, point) is not null;

    private bool _dialogAccepted;
    private object? _dialogValue;

    internal IPlatform? Platform { get; set; }

    public Form()
    {
        _toolTipTimer = new System.Threading.Timer(
            OnToolTipTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

        App.ThemeChanged += OnThemeChanged;
    }

    internal void OnPointerMove(Point point, KeyModifiers modifiers = KeyModifiers.None)
    {
        _lastPointerPosition = point;

        // при захвате цепочка строится от захватившего, а не от того, над кем
        // курсор: иначе предпросмотр посыпался бы в чужое поддерево
        UIElement? target = _mouseCapture ?? _pressedElement ?? HitTestAll(point);

        var moveArgs = new MouseMoveEventArgs(point);

        // предпросмотр от корня к цели, до того как движение получит она сама.
        // Работает и с зажатой кнопкой — именно там он и нужен, чтобы предок
        // мог следить за перетаскиванием над своими потомками
        if (target is not null)
        {
            List<UIElement> chain = [];

            for (UIElement? current = target; current is not null; current = current.Parent)
                chain.Add(current);

            chain.Reverse();

            foreach (UIElement element in chain)
                element.RaisePreviewMouseMove(moveArgs);
        }

        if (_mouseCapture is not null)
        {
            _mouseCapture.RaiseMouseMove(point);
            return;
        }

        if (_pressedElement is not null)
        {
            _pressedElement.RaiseMouseMove(point);
            return;
        }

        UIElement? hit = target;

        if (hit != _hoveredElement)
        {
            // в аргументах указываем «откуда» и «куда», чтобы обработчик
            // мог отличить переход внутрь потомка от выхода наружу
            _hoveredElement?.RaiseMouseExit(point, hit);
            hit?.RaiseMouseEnter(point, _hoveredElement);

            _hoveredElement = hit;

            ScheduleToolTip(hit);
        }

        hit?.RaiseMouseMove(point);

        CursorKind cursor = hit?.EffectiveCursor ?? CursorKind.Arrow;

        if (cursor != _lastCursor)
        {
            _lastCursor = cursor;
            PlatformWindow?.SetCursor(cursor);
        }

        if (IsInspectorEnabled)
        {
            InspectedElement = !IsInsideInspector(point) && Content is not null
                ? HitTester.HitTest(Content, point)
                : null;

            InvalidateVisual();
        }
    }

    internal void OnPointerLeaveWindow()
    {
        HideToolTip();
        _hoveredElement?.RaiseMouseExit(_lastPointerPosition, null);
        _hoveredElement = null;
    }

    internal void OnPointerDown(Point point, MouseButton button = MouseButton.Left, KeyModifiers modifiers = KeyModifiers.None)
    {
        HideToolTip();

        if (button == MouseButton.Left && _flyouts.Count > 0 && !IsInsideAnyFlyout(point))
        {
            CloseAllFlyouts();
            return;
        }

        UIElement? hit = HitTestAll(point);
        if (hit is { IsEnabled: false }) return;

        UpdateClickCount(point, button);

        if (IsInspectorEnabled && _inspectorGrid is not null && !IsInsideInspector(point))
        {
            UIElement? picked = Content is not null ? HitTester.HitTest(Content, point) : null;

            if (picked is not null)
            {
                _inspectorGrid.SelectedObject = picked;
                Invalidate();
                return;
            }
        }

        var downArgs = new MouseButtonEventArgs(button, MouseButtonState.Down, point, modifiers);

        if (hit is not null)
        {
            List<UIElement> chain = [];

            for (UIElement? current = hit; current is not null; current = current.Parent)
                chain.Add(current);

            chain.Reverse();

            foreach (UIElement element in chain)
                element.RaisePreviewMouseDown(downArgs);
        }

        if (button == MouseButton.Left)
            _pressedElement = hit;

        hit?.RaiseMouseDown(downArgs);

        if (button == MouseButton.Left && hit is not null)
            _focusDispatcher.FocusElement(hit);
    }

    internal void OnPointerUp(Point point, MouseButton button = MouseButton.Left, KeyModifiers modifiers = KeyModifiers.None)
    {
        UIElement? hit = HitTestAll(point);

        var upArgs = new MouseButtonEventArgs(button, MouseButtonState.Up, point, modifiers);

        if (button == MouseButton.Left)
        {
            // цепочка та же, что при нажатии: кто следил за press через
            // предпросмотр, должен узнать и об отпускании
            for (UIElement? current = _pressedElement; current is not null; current = current.Parent)
                current.RaisePreviewMouseUp(upArgs);

            _pressedElement?.RaiseMouseUp(upArgs);

            // захвативший должен узнать об отпускании, даже если нажатие
            // пришлось на его потомка
            if (_mouseCapture is not null && !ReferenceEquals(_mouseCapture, _pressedElement))
                _mouseCapture.RaiseMouseUp(upArgs);

            if (_mouseCapture is not null)
            {
                _mouseCapture = null;
                PlatformWindow?.ReleaseMouseCapture();
            }

            // клик = нажатие и отпускание на одном элементе
            if (hit is not null && ReferenceEquals(hit, _pressedElement))
                BubbleClick(hit, button, point);

            _pressedElement = null;
            return;
        }

        hit?.RaiseMouseUp(upArgs);

        // правая и средняя не требуют совпадения с нажатием:
        // захвата для них нет, поэтому клик по факту отпускания
        if (hit is not null)
            BubbleClick(hit, button, point);
    }

    private void BubbleClick(UIElement hit, MouseButton button, Point point)
    {
        var args = new MouseClickEventArgs(button, MouseButtonState.Up, point, _clickCount);

        for (UIElement? current = hit; current is not null; current = current.Parent)
        {
            current.RaiseClick(args);
            if (args.Handled) break;
        }
    }

    private void UpdateClickCount(Point point, MouseButton button)
    {
        long now = Environment.TickCount64;

        bool sameSpot =
            Math.Abs(point.X - _lastClickPoint.X) <= DoubleClickSlop &&
            Math.Abs(point.Y - _lastClickPoint.Y) <= DoubleClickSlop;

        bool inTime = now - _lastClickTicks <= DoubleClickIntervalMs;

        _clickCount = inTime && sameSpot && button == _lastClickButton ? _clickCount + 1 : 1;

        _lastClickTicks = now;
        _lastClickPoint = point;
        _lastClickButton = button;
    }

    /// <summary>Забрать себе движения мыши до отпускания кнопки.
    /// Нажатие остаётся у того, на кого попали, поэтому клик по потомку
    /// захватившего элемента продолжает работать как обычно.</summary>
    internal void CaptureMouse(UIElement element)
    {
        if (ReferenceEquals(_mouseCapture, element)) return;

        _mouseCapture = element;
        PlatformWindow?.CaptureMouse();
    }

    internal void ReleaseMouseCapture(UIElement element)
    {
        if (!ReferenceEquals(_mouseCapture, element)) return;

        _mouseCapture = null;
        PlatformWindow?.ReleaseMouseCapture();
    }

    /// <summary>Захват отобрала система. Своё состояние сбрасываем как при
    /// отпускании кнопки, иначе перетаскивание не завершится никогда.</summary>
    internal void OnCaptureLost()
    {
        UIElement? captured = _mouseCapture;
        UIElement? pressed = _pressedElement;

        _mouseCapture = null;
        _pressedElement = null;

        var args = new MouseButtonEventArgs(
            MouseButton.Left, MouseButtonState.Up, _lastPointerPosition, KeyModifiers.None);

        pressed?.RaiseMouseUp(args);

        if (captured is not null && !ReferenceEquals(captured, pressed))
            captured.RaiseMouseUp(args);
    }

    internal void OnKeyDown(Key key, KeyModifiers modifiers, bool isRepeat)
    {
        Keyboard.OnDown(key, modifiers);

        if (key == Key.F12 || (key == Key.I && modifiers.HasFlag(KeyModifiers.Control) && modifiers.HasFlag(KeyModifiers.Shift)))
        {
            ToggleInspector();
            return;
        }

        var args = new KeyEventArgs(key, modifiers);

        // превью идёт от корня к сфокусированному элементу
        UIElement? focused = _focusDispatcher.FocusedElement;

        if (focused is not null)
        {
            List<UIElement> chain = [];

            for (UIElement? current = focused; current is not null; current = current.Parent)
                chain.Add(current);

            chain.Reverse();

            foreach (UIElement element in chain)
            {
                element.RaisePreviewKeyDown(args);
                if (args.Handled) return;
            }
        }

        for (UIElement? current = focused; current is not null; current = current.Parent)
        {
            current.RaiseKeyDown(args);
            if (args.Handled) break;
        }

        if (!args.Handled && key == Key.Tab && Content is not null)
        {
            if (modifiers.HasFlag(KeyModifiers.Shift))
                _focusDispatcher.MovePrevious(Content);
            else
                _focusDispatcher.MoveNext(Content);
        }
    }

    internal void OnKeyUp(Key key, KeyModifiers modifiers)
    {
        Keyboard.OnUp(key, modifiers);

        var args = new KeyEventArgs(key, modifiers);

        for (UIElement? current = _focusDispatcher.FocusedElement; current is not null; current = current.Parent)
        {
            current.RaiseKeyUp(args);
            if (args.Handled) break;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (Content is not null)
            ApplyTheme(Content);

        // оверлеи живут отдельно от Content: без этого открытые инспектор,
        // меню или флаут остаются в старой теме до закрытия
        foreach (UIElement overlay in _overlays.ToArray())
            ApplyTheme(overlay);

        Invalidate();
    }

    /// <summary>Оформить поддерево по текущей теме. Вызывается при
    /// присоединении к форме и при смене темы.</summary>
    internal void ApplyTheme(UIElement root)
    {
        Walk(root, App.Theme.Apply);
    }

    /// <summary>Сменить тему с расходящейся волной от точки.</summary>
    public void SwitchTheme(Theme theme, Point origin)
    {
        _themeRippleOrigin = origin;
        _themeRippleColor = theme.Colors.Background;
        _themeRippleActive = true;

        float maxRadius = MathF.Sqrt(
            ClientSize.Width * ClientSize.Width + ClientSize.Height * ClientSize.Height);

        // тему применяем сразу, а волна прикрывает момент перекраски
        App.Theme = theme;

        var animation = new Animation<float>(
            this, "theme-ripple", 0f, maxRadius, TimeSpan.FromMilliseconds(450),
            Interpolators.Float,
            value => { _themeRippleRadius = value; InvalidateVisual(); },
            Easing.EaseOut,
            completed: () => { _themeRippleActive = false; InvalidateVisual(); });

        AddAnimation(animation);
    }

    internal (bool Active, Point Origin, float Radius, Color Color) ThemeRipple =>
        (_themeRippleActive, _themeRippleOrigin, _themeRippleRadius, _themeRippleColor);

    public void Show()
    {
        DesktopWindow?.SetOpacity(_opacity);
        DesktopWindow?.SetTitle(Title);
        PlatformWindow?.Show();
        Shown?.Invoke(this, EventArgs.Empty);
    }

    public void Close() => PlatformWindow?.Close();

    public void Invoke(Action action) => PlatformWindow?.Invoke(action);

    public UIElement? FindByName(string name) => NameScope.Find(name);
    public T? FindByName<T>(string name) where T : UIElement => NameScope.Find<T>(name);

    // ===== Присоединение поддерева к форме =====

    /// <summary>Показать оверлей: присоединить поддерево (тема, шрифт, имена,
    /// OnAttached) и положить поверх содержимого. Единственный путь
    /// в список оверлеев — флауты, тосты, подсказки, меню и инспектор
    /// проходят через него.</summary>
    /// <remarks>Присоединение идёт до измерения: тема задаёт шрифты
    /// и отступы, а позиция оверлея считается из DesiredSize.</remarks>
    private void AttachOverlay(UIElement content)
    {
        if (_overlays.Contains(content)) return;

        ZfContract.Require(
            content.Owner is null,
            "Оверлей уже принадлежит форме. Один элемент не может быть " +
            "оверлеем двух форм одновременно: Owner перезапишется, " +
            "и первая форма потеряет с ним связь.");

        content.Owner = this;
        AttachTree(content);

        _overlays.Add(content);
    }

    /// <summary>Убрать оверлей и отсоединить поддерево. Без отсоединения
    /// закрытый оверлей остаётся в NameScope, его анимации продолжают
    /// крутиться, а ссылки на него держат диспетчеры ввода и фокуса.</summary>
    private bool DetachOverlay(UIElement content)
    {
        if (!_overlays.Remove(content)) return false;

        DetachTree(content);
        content.Owner = null;

        return true;
    }

    internal void AttachTree(UIElement root)
    {
        Walk(root, element =>
        {
            NameScope.Register(element);

            // тема применяется до первого layout, чтобы размеры считались
            // уже с правильными шрифтами и отступами
            App.Theme.Apply(element);

            element.RaiseAttached();
        });
    }

    internal void DetachTree(UIElement root)
    {
        Walk(root, element =>
        {
            NameScope.Unregister(element);
            element.RaiseDetached();
        });

        // иначе выброшенное дерево остаётся живым через ссылки диспетчера
        if (_hoveredElement is not null && IsInTree(root, _hoveredElement))
            _hoveredElement = null;

        if (_pressedElement is not null && IsInTree(root, _pressedElement))
            _pressedElement = null;

        if (_mouseCapture is not null && IsInTree(root, _mouseCapture))
            _mouseCapture = null;

        if (_dropTarget is not null && IsInTree(root, _dropTarget))
        {
            _dropTarget = null;
            _dropEffect = DragDropEffect.None;
        }

        if (_focusDispatcher.FocusedElement is { } focused && IsInTree(root, focused))
            _focusDispatcher.ClearFocus();

        // как и остальные ссылки выше — только если сбрасываемый элемент
        // действительно в отсоединяемом поддереве. Безусловный сброс стал
        // заметен, когда через DetachTree пошло скрытие подсказки:
        // оно случается на каждом движении мыши и гасило бы выбор
        // инспектора вместе с ним
        if (InspectedElement is not null && IsInTree(root, InspectedElement))
            InspectedElement = null;

        if (_toolTipOwner is not null && IsInTree(root, _toolTipOwner))
            _toolTipOwner = null;

        for (int i = _animations.Count - 1; i >= 0; i--)
        {
            if (_animations[i].Target is not UIElement element || !IsInTree(root, element))
                continue;

            IAnimation animation = _animations[i];
            _animations.RemoveAt(i);

            // цель уходит из дерева: ни значение доводить, ни completed
            // звать не нужно — приводить в порядок больше нечего
            animation.Cancel(applyFinalValue: false);
        }

        if (_animations.Count == 0)
            PlatformWindow?.Frames.Stop();
    }

    private static bool IsInTree(UIElement root, UIElement candidate)
    {
        for (UIElement? current = candidate; current is not null; current = current.Parent)
            if (ReferenceEquals(current, root))
                return true;

        return false;
    }

    private static void Walk(UIElement root, Action<UIElement> action)
    {
        action(root);

        switch (root)
        {
            case WrapControl wrap when wrap.Child is not null:
                Walk(wrap.Child, action);
                break;

            case PanelControl panel:
                foreach (var child in panel.Children)
                    Walk(child, action);
                break;
        }
    }

    private int _layoutDepth;

    internal void PerformLayout()
    {
        // защита от рекурсии: Invalidate во время раскладки запустил бы её заново
        if (_layoutDepth > 0)
        {
            ZfContract.Fail(
                "PerformLayout вызван повторно во время раскладки. " +
                "Обычно это Invalidate из MeasureOverride или ArrangeOverride: " +
                "измените там геометрию напрямую либо отложите Invalidate " +
                "до конца прохода.");

            return;
        }

        _layoutDepth++;

        try
        {
            if (Content is not null)
            {
                Content.Measure(ClientSize);
                Content.Arrange(new Rectangle(Point.Empty, ClientSize));
            }

            foreach (var overlay in _overlays)
            {
                overlay.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
                overlay.Arrange(new Rectangle(overlay.Position, overlay.DesiredSize));
            }
        }
        finally
        {
            _layoutDepth--;
        }
    }

    private Rectangle? _dirtyRegion;

    internal Rectangle? TakeDirtyRegion()
    {
        Rectangle? region = _dirtyRegion;
        _dirtyRegion = null;
        return region;
    }

    internal void InvalidateRect(Rectangle bounds)
    {
        _dirtyRegion = _dirtyRegion is { } existing ? existing.Union(bounds) : bounds;
        PlatformWindow?.Invalidate(bounds);
    }

    /// <summary>Перерисовать всю клиентскую область без пересчёта раскладки.</summary>
    internal void InvalidateVisual()
    {
        _dirtyRegion = new Rectangle(Point.Empty, ClientSize);
        PlatformWindow?.Invalidate(null);
    }

    internal void Invalidate()
    {
        PerformLayout();

        // полная перерисовка: копим всю клиентскую область
        _dirtyRegion = new Rectangle(Point.Empty, ClientSize);
        PlatformWindow?.Invalidate(null);
    }

    internal void AddAnimation(IAnimation animation)
    {
        // одна анимация на связку «объект + свойство».
        // Вытесняемую снимаем с вызовом её completed, иначе состояние,
        // которое она должна была привести в порядок, останется в середине —
        // именно из-за этого PageControl оставлял страницы висеть
        for (int i = _animations.Count - 1; i >= 0; i--)
        {
            IAnimation existing = _animations[i];

            if (!ReferenceEquals(existing.Target, animation.Target) ||
                existing.Key != animation.Key)
                continue;

            _animations.RemoveAt(i);

            // без доведения значения: новая анимация начнёт со своего from,
            // и прыжок в конец дал бы мелькание
            existing.Cancel(applyFinalValue: false);
        }

        _animations.Add(animation);

        if (_animations.Count == 1)
        {
            _lastTickTicks = Environment.TickCount64;
            PlatformWindow?.Frames.Start(FrameIntervalMs);
        }
    }

    internal void RemoveAnimation(object target, string key)
    {
        for (int i = _animations.Count - 1; i >= 0; i--)
        {
            IAnimation existing = _animations[i];

            if (!ReferenceEquals(existing.Target, target) || existing.Key != key)
                continue;

            _animations.RemoveAt(i);
            existing.Cancel(applyFinalValue: false);
        }

        if (_animations.Count == 0)
            PlatformWindow?.Frames.Stop();
    }

    /// <summary>Снова выдавать кадры, если анимации ещё не закончились.
    /// Вызывается при возврате приложения из фона.</summary>
    internal void ResumeFrames()
    {
        if (_animations.Count == 0) return;

        // за время в фоне прошло сколько угодно времени; без сброса первая же
        // итерация продвинула бы анимации сразу до конца
        _lastTickTicks = Environment.TickCount64;
        PlatformWindow?.Frames.Start(FrameIntervalMs);
    }

    internal void Tick()
    {
        long now = Environment.TickCount64;
        var elapsed = TimeSpan.FromMilliseconds(now - _lastTickTicks);
        _lastTickTicks = now;

        bool wholeWindow = false;

        // по снимку, а не по живому списку: Advance вызывает completed
        // прямо внутри себя, а тот может и снять анимации, и добавить —
        // переход страницы делает ровно это. Индексы при таком раскладе
        // разъезжаются под ногами
        IAnimation[] running = [.. _animations];

        foreach (IAnimation animation in running)
        {
            // могли снять из completed соседней анимации
            if (!_animations.Contains(animation)) continue;

            bool alive = animation.Advance(elapsed);

            // перерисовываем цель независимо от того, дожила ли анимация
            // до следующего кадра: последний её кадр тоже надо показать
            switch (animation.Target)
            {
                // анимация самой формы — например, волна смены темы —
                // выходит за пределы любого отдельного элемента
                case Form: wholeWindow = true; break;
                case UIElement element: element.InvalidateVisual(); break;
            }

            if (!alive) _animations.Remove(animation);
        }

        if (_animations.Count == 0)
            PlatformWindow?.Frames.Stop();

        if (wholeWindow) InvalidateVisual();
    }

    // ===== Flyout API =====

    public void ShowFlyout(UIElement anchor, UIElement content, FlyoutPlacement placement = FlyoutPlacement.Bottom)
    {
        AttachOverlay(content);

        content.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

        Point anchorPos = anchor.GetAbsolutePosition();
        Size anchorSize = anchor.ActualSize;
        Size contentSize = content.DesiredSize;

        content.Position = placement switch
        {
            FlyoutPlacement.Bottom => new Point(anchorPos.X, anchorPos.Y + anchorSize.Height),
            FlyoutPlacement.Top => new Point(anchorPos.X, anchorPos.Y - contentSize.Height),
            FlyoutPlacement.Right => new Point(anchorPos.X + anchorSize.Width, anchorPos.Y),
            FlyoutPlacement.Left => new Point(anchorPos.X - contentSize.Width, anchorPos.Y),
            _ => anchorPos,
        };

        _flyouts.Add(content);

        Invalidate();
    }

    public void CloseFlyout(UIElement content)
    {
        if (!DetachOverlay(content)) return;

        _flyouts.Remove(content);

        FlyoutClosed?.Invoke(this, content);
        Invalidate();
    }

    public void CloseAllFlyouts()
    {
        if (_flyouts.Count == 0) return;

        // копия: обработчики события могут открыть новый флаут,
        // и коллекция изменится во время обхода
        UIElement[] closing = [.. _flyouts];

        foreach (UIElement flyout in closing)
            DetachOverlay(flyout);

        _flyouts.Clear();

        foreach (UIElement flyout in closing)
            FlyoutClosed?.Invoke(this, flyout);

        Invalidate();
    }

    /// <summary>Положить элемент поверх содержимого. В отличие от ShowFlyout
    /// не закрывается по клику мимо и не участвует в логике всплывашек.</summary>
    public void AddOverlay(UIElement content)
    {
        AttachOverlay(content);
        Invalidate();
    }

    public void RemoveOverlay(UIElement content)
    {
        if (!DetachOverlay(content)) return;

        Invalidate();
    }

    // ==== Dialog ====

    private bool _isClosed;

    /// <summary>Платформа разрушила окно. Единственная точка, где завершается
    /// ожидание диалога: различать источник закрытия незачем, а вот пропустить
    /// его нельзя — ShowDialogAsync повиснет навсегда.</summary>
    internal void OnWindowClosed()
    {
        // DestroyWindow в Win32 шлёт и WM_DESTROY, и WM_NCDESTROY;
        // на X11 Close() может прийти и от нас, и от WM_DELETE_WINDOW
        if (_isClosed) return;
        _isClosed = true;

        s_openForms.Remove(this);

        // таймер кадров живёт в окне, которого больше нет
        PlatformWindow?.Frames.Stop();

        _dialogClosed?.TrySetResult();

        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Показать диалог и дождаться результата. Требует платформы
    /// с вложенным циклом; там, где его нет, используйте ShowDialogAsync.</summary>
    public DialogResult<T> ShowDialog<T>(Form owner)
    {
        IPlatform platform = owner.Platform
            ?? throw new InvalidOperationException("Владелец диалога ещё не привязан к платформе.");

        if (platform is not INestedLoopSupport loop)
            throw new NotSupportedException(
                $"Платформа {platform.GetType().Name} не поддерживает вложенный цикл. " +
                "Используйте ShowDialogAsync.");

        BeginDialog(owner, platform);

        try
        {
            loop.RunNestedLoop(PlatformWindow!);
        }
        finally
        {
            EndDialog(owner);
        }

        return Result<T>();
    }

    /// <summary>Показать диалог, не блокируя вызывающий код. Работает
    /// на любой платформе, в том числе там, где вложенного цикла нет.</summary>
    public async Task<DialogResult<T>> ShowDialogAsync<T>(Form owner)
    {
        IPlatform platform = owner.Platform
            ?? throw new InvalidOperationException("Владелец диалога ещё не привязан к платформе.");

        BeginDialog(owner, platform);

        try
        {
            await _dialogClosed!.Task;
        }
        finally
        {
            EndDialog(owner);
        }

        return Result<T>();
    }

    private TaskCompletionSource? _dialogClosed;

    private void BeginDialog(Form owner, IPlatform platform)
    {
        IsDialog = true;
        Platform = platform;

        _dialogAccepted = false;
        _dialogValue = null;

        // завершения ждём и синхронно, и асинхронно: источник один —
        // закрытие окна, откуда бы оно ни пришло
        _dialogClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        platform.CreateWindow(this);

        // модальность — это заглушённый владелец, а не вложенный цикл:
        // первое нужно везде, второе только на настольных платформах
        owner.PlatformWindow?.SetEnabled(false);

        Show();
    }

    private void EndDialog(Form owner)
    {
        if (owner.PlatformWindow is { } ownerWindow)
        {
            ownerWindow.SetEnabled(true);
            ownerWindow.Activate();
        }

        _dialogClosed = null;
    }

    private DialogResult<T> Result<T>() =>
        _dialogAccepted && _dialogValue is T typed
            ? new DialogResult<T>(true, typed)
            : DialogResult<T>.Cancelled();

    /// <summary>Закрыть диалог с результатом.</summary>
    public void Accept(object? value = null)
    {
        _dialogAccepted = true;
        _dialogValue = value;
        Close();
    }

    public void Cancel()
    {
        _dialogAccepted = false;
        _dialogValue = null;
        Close();
    }

    // ==== Toast =====

    public void ShowToast(string message, int durationMs = 3000, ToastPosition position = ToastPosition.BottomRight)
    {
        var toast = new Border
        {
            BorderColor = new Color(255, 60, 60, 60),
            BorderWidth = 1,
            Background = new Color(235, 45, 45, 45),
            Padding = new Thickness(14, 10),
            IsHitTestVisible = false,
            Child = new Label { Text = message, TextColor = Colors.White },
        };

        toast.Owner = this;
        AttachOverlay(toast);

        toast.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

        _toasts.Add(toast);
        ArrangeToasts(position);
        Invalidate();

        System.Threading.Timer? timer = null;
        timer = new System.Threading.Timer(_ =>
        {
            Invoke(() =>
            {
                DetachOverlay(toast);
                _toasts.Remove(toast);
                ArrangeToasts(position);   // оставшиеся подтягиваются на освободившееся место
                Invalidate();
            });

            timer?.Dispose();
        }, null, durationMs, Timeout.Infinite);
    }

    private void ArrangeToasts(ToastPosition position)
    {
        const float margin = 16f;
        const float gap = 8f;

        bool fromTop = position is ToastPosition.TopRight or ToastPosition.TopCenter;
        bool centered = position is ToastPosition.TopCenter or ToastPosition.BottomCenter;

        float offset = margin;

        // снизу — новые появляются ниже, старые уезжают вверх, поэтому идём с конца
        IEnumerable<UIElement> order = fromTop ? _toasts : Enumerable.Reverse(_toasts);

        foreach (var toast in order)
        {
            Size size = toast.DesiredSize;

            float x = centered
                ? (ClientSize.Width - size.Width) / 2f
                : ClientSize.Width - size.Width - margin;

            float y = fromTop ? offset : ClientSize.Height - size.Height - offset;

            toast.Position = new Point(x, y);
            offset += size.Height + gap;
        }
    }

    // ===== ToolTip =====

    private void ScheduleToolTip(UIElement? target)
    {
        HideToolTip();

        _toolTipOwner = target is not null && !string.IsNullOrEmpty(target.ToolTip) ? target : null;

        _toolTipTimer.Change(
            _toolTipOwner is not null ? ToolTipDelay : Timeout.Infinite,
            Timeout.Infinite);
    }

    // Вызывается на потоке пула — обязательно маршалим на UI-поток
    private void OnToolTipTimerElapsed(object? state) => Invoke(ShowToolTipCore);

    private void ShowToolTipCore()
    {
        if (_toolTipOwner is null || string.IsNullOrEmpty(_toolTipOwner.ToolTip))
            return;

        var tip = new Border
        {
            BorderColor = Colors.Black,
            BorderWidth = 1,
            Background = new Color(240, 250, 250, 210),
            Padding = new Thickness(6, 3),
            IsHitTestVisible = false,
            Child = new Label
            {
                Text = _toolTipOwner.ToolTip,
                TextColor = Colors.Black,
                IsHitTestVisible = false,
            },
        };

        AttachOverlay(tip);

        tip.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

        // чуть ниже-правее курсора, как принято в системных подсказках
        float x = _lastPointerPosition.X + 12;
        float y = _lastPointerPosition.Y + 20;

        // не даём вылезти за пределы клиентской области
        if (x + tip.DesiredSize.Width > ClientSize.Width)
            x = Math.Max(0, ClientSize.Width - tip.DesiredSize.Width);

        if (y + tip.DesiredSize.Height > ClientSize.Height)
            y = Math.Max(0, _lastPointerPosition.Y - tip.DesiredSize.Height - 4);

        tip.Position = new Point(x, y);
        tip.Owner = this;

        tip.Position = new Point(x, y);

        _activeToolTip = tip;

        Invalidate();
    }

    private void HideToolTip()
    {
        _toolTipTimer.Change(Timeout.Infinite, Timeout.Infinite);

        if (_activeToolTip is not null)
        {
            DetachOverlay(_activeToolTip);
            _activeToolTip = null;
            Invalidate();
        }
    }

    // ===== Клавиатура =====

    private PropertyGrid? _inspectorGrid;

    private void ToggleInspector()
    {
        IsInspectorEnabled = !IsInspectorEnabled;

        if (IsInspectorEnabled)
        {
            _inspectorGrid = new PropertyGrid
            {
                Size = new Size(320, ClientSize.Height),
                Position = new Point(Math.Max(0, ClientSize.Width - 320), 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };

            AttachOverlay(_inspectorGrid);
        }
        else if (_inspectorGrid is not null)
        {
            DetachOverlay(_inspectorGrid);
            _inspectorGrid = null;
        }

        Invalidate();
    }

    internal void OnTextInput(char c)
    {
        _focusDispatcher.FocusedElement?.RaiseTextInput(c);
    }

    // ===== Диспетчинг ввода =====

    private UIElement? HitTestAll(Point point)
    {
        for (int i = _overlays.Count - 1; i >= 0; i--)
        {
            var hit = HitTester.HitTest(_overlays[i], point);
            if (hit is not null)
                return hit;
        }

        return Content is not null ? HitTester.HitTest(Content, point) : null;
    }

    private bool IsInsideAnyFlyout(Point point)
    {
        foreach (var flyout in _flyouts)
            if (HitTester.HitTest(flyout, point) is not null)
                return true;

        return false;
    }

    internal void OnMouseWheel(Point point, int delta, int horizontalDelta = 0)
    {
        UIElement? hit = HitTestAll(point);
        if (hit is null) return;

        var args = new MouseWheelEventArgs(point, delta, horizontalDelta);

        for (UIElement? current = hit; current is not null; current = current.Parent)
        {
            current.RaiseMouseWheel(args);
            if (args.Handled)
                break;
        }
    }

    /// <summary>Окно потеряло фокус: об отпусканиях клавиш мы больше
    /// не узнаем, поэтому считаем, что не нажато ничего.</summary>
    internal void OnWindowFocusLost() => Keyboard.Reset();

    // вызывается платформой, когда состояние сменил сам пользователь —
    // без обратного вызова в SetWindowState, иначе получим петлю
    internal void SetWindowStateFromPlatform(WindowState state)
    {
        if (_windowState == state) return;
        _windowState = state;
        WindowStateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void OnContextMenu(Point point)
    {
        UIElement? hit = HitTestAll(point);

        // меню ищем вверх по дереву: если у самой кнопки его нет,
        // спрашиваем панель, потом форму
        for (UIElement? current = hit; current is not null; current = current.Parent)
        {
            if (current.ContextMenu is { Count: > 0 } items)
            {
                ShowContextMenu(items, point);
                return;
            }
        }
    }

    public void ShowContextMenu(List<MenuItem> items, Point position)
    {
        CloseAllFlyouts();

        var menu = new MenuList { Items = items };
        menu.ItemInvoked += (_, _) => CloseAllFlyouts();

        AttachOverlay(menu);

        menu.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

        float x = Math.Min(position.X, Math.Max(0, ClientSize.Width - menu.DesiredSize.Width));
        float y = Math.Min(position.Y, Math.Max(0, ClientSize.Height - menu.DesiredSize.Height));

        menu.Position = new Point(x, y);

        _flyouts.Add(menu);   // закроется кликом мимо — как и положено меню

        Invalidate();
    }

    private DragDropEffect _dropEffect;

    /// <summary>Перетаскивание вошло в окно. Возвращённый эффект источник
    /// показывает курсором.</summary>
    internal DragDropEffect OnDragEnterWindow(DragDropData data, Point point, KeyModifiers modifiers) =>
        UpdateDropTarget(data, point, modifiers);

    internal DragDropEffect OnDragOverWindow(DragDropData data, Point point, KeyModifiers modifiers) =>
        UpdateDropTarget(data, point, modifiers);

    /// <summary>Перетаскивание ушло из окна или было отменено.</summary>
    internal void OnDragLeaveWindow()
    {
        _dropTarget?.RaiseDragLeave();
        _dropTarget = null;
    }

    internal DragDropEffect OnDropWindow(DragDropData data, Point point, KeyModifiers modifiers)
    {
        // приёмник пересчитываем: бросок может прийти без предшествующего
        // over — например, если источник дал только enter и сразу drop
        DragDropEffect effect = UpdateDropTarget(data, point, modifiers);

        UIElement? target = _dropTarget;
        _dropTarget = null;

        if (target is null || effect == DragDropEffect.None)
        {
            target?.RaiseDragLeave();

            return DragDropEffect.None;
        }

        var args = new DragDropEventArgs(data, point, modifiers) { Effect = effect };

        target.RaiseDrop(args);

        // DragLeave после броска обязателен: приёмник подсветился на enter,
        // и снять подсветку ему больше негде
        target.RaiseDragLeave();

        return args.Effect;
    }

    /// <summary>Найти приёмник под курсором, разослать enter и leave при
    /// смене и спросить эффект.</summary>
    private DragDropEffect UpdateDropTarget(DragDropData data, Point point, KeyModifiers modifiers)
    {
        _lastPointerPosition = point;

        UIElement? target = FindDropTarget(HitTestAll(point));

        if (!ReferenceEquals(target, _dropTarget))
        {
            _dropTarget?.RaiseDragLeave();
            _dropTarget = target;

            if (target is not null)
            {
                var enterArgs = new DragDropEventArgs(data, point, modifiers);

                target.RaiseDragEnter(enterArgs);

                // эффект, выставленный на входе, — начальное значение
                // для последующих over: приёмнику не нужно повторять его
                _dropEffect = enterArgs.Effect;
            }
        }

        if (_dropTarget is null) return DragDropEffect.None;

        var args = new DragDropEventArgs(data, point, modifiers) { Effect = _dropEffect };

        _dropTarget.RaiseDragOver(args);
        _dropEffect = args.Effect;

        return _dropEffect;
    }

    /// <summary>Ближайший элемент с AllowDrop, начиная с попавшего.
    /// Так подсветку можно включить на панели, не размечая каждую строку.</summary>
    private static UIElement? FindDropTarget(UIElement? hit)
    {
        for (UIElement? current = hit; current is not null; current = current.Parent)
            if (current is { AllowDrop: true, IsEnabled: true })
                return current;

        return null;
    }

    public void Dispose()
    {
        App.ThemeChanged -= OnThemeChanged;
        _toolTipTimer?.Dispose();
    }
}