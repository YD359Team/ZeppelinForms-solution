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
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Forms;

public partial class Form : IDisposable
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

            // то же с анимациями: добавленные до создания окна лежат
            // в часах, но выдавать кадры было некому — просим их здесь
            ReviewFrames();
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
    private CursorKind _lastCursor = CursorKind.Arrow;
    private readonly FocusDispatcher _focusDispatcher = new();

    /// <summary>Идентификатор мыши. Единица, а не ноль — так же нумерует
    /// мышь W3C Pointer Events, и браузерный бэкенд отдаёт pointerId как есть.</summary>
    public const int MousePointerId = 1;

    /// <summary>Живые контакты по идентификатору. Пуст, пока ничего
    /// не нажато: у большинства форм он таким и остаётся.</summary>
    private readonly Dictionary<int, PointerContact> _contacts = [];

    private int? _primaryContactId;

    /// <summary>Сколько контактов держат системный захват. Считать нужно
    /// потому, что IPlatformWindow.CaptureMouse() безаргументен — захват
    /// в системе один на окно, и второй палец, отпустившись, снял бы
    /// захват у первого.</summary>
    private int _platformCaptureCount;

    /// <summary>Контакт, который обрабатывается прямо сейчас. Нужен
    /// CaptureMouse: элемент зовёт его изнутри обработчика и знать
    /// идентификатор контакта не обязан.</summary>
    private PointerContact? _dispatching;

    private PointerContact? PrimaryContact =>
        _primaryContactId is int id && _contacts.TryGetValue(id, out PointerContact? c) ? c : null;

    // ===== ToolTip =====
    public int ToolTipDelay { get; set; } = 700;
    private IDisposable? _toolTipWake;
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
        App.ThemeChanged += OnThemeChanged;
        _focusDispatcher.FocusChanged += OnFocusChangedForKeyboard;
    }

    /// <summary>Клавиатура следует за фокусом: поле получило его — показываем,
    /// ушёл на кнопку или в никуда — прячем.</summary>
    private void OnFocusChangedForKeyboard(object? sender, UIElement? focused)
    {
        if (PlatformWindow is not ISoftKeyboard keyboard) return;

        if (focused is { AcceptsTextInput: true } input)
            keyboard.ShowSoftKeyboard(input.SoftKeyboardKind);
        else
            keyboard.HideSoftKeyboard();
    }

    // ===== Переходники со старых сигнатур =====
    // Бэкенды пока присылают мышь позиционными аргументами. Менять их
    // в этом проходе не нужно: точка входа одна, а конвейер под ней новый.

    internal void OnPointerMove(Point point, KeyModifiers modifiers = KeyModifiers.None) =>
        OnPointerMove(new PointerEventArgs(
            MousePointerId, PointerKind.Mouse, point, MouseButton.Left, 1f, modifiers));

    internal void OnPointerDown(PointerEventArgs e)
    {
        HideToolTip();

        if (e.Button == MouseButton.Left && _flyouts.Count > 0 && !IsInsideAnyFlyout(e.Location))
        {
            CloseAllFlyouts();
            return;
        }

        UIElement? hit = HitTestAll(e.Location);
        if (hit is { IsEnabled: false }) return;

        if (IsInspectorEnabled && _inspectorGrid is not null && !IsInsideInspector(e.Location))
        {
            UIElement? picked = Content is not null ? HitTester.HitTest(Content, e.Location) : null;

            if (picked is not null)
            {
                _inspectorGrid.SelectedObject = picked;
                Invalidate();
                return;
            }
        }

        // та же кнопка на живом контакте: мышь прислала второе нажатие
        // без отпускания. Контакт продолжается, меняется только маска —
        // заводить второй с тем же идентификатором нельзя
        if (_contacts.TryGetValue(e.PointerId, out PointerContact? existing))
        {
            existing.Buttons |= e.Button.ToFlag();
            existing.Update(e.Location, e.Timestamp);

            DispatchDown(existing, hit, e, isNew: false);
            return;
        }

        bool isPrimary = _contacts.Count == 0;

        var contact = new PointerContact
        {
            Id = e.PointerId,
            Kind = e.Kind,
            IsPrimary = isPrimary,
            DownLocation = e.Location,
            DownTimestamp = e.Timestamp,
            Buttons = e.Button.ToFlag(),
            Pressed = hit,
            Chain = BuildChain(hit),
        };

        contact.Update(e.Location, e.Timestamp);

        _contacts[e.PointerId] = contact;

        if (isPrimary) _primaryContactId = e.PointerId;

        // кратность считаем только по ведущему контакту: два пальца подряд
        // по одному месту — это не двойной щелчок
        if (isPrimary) UpdateClickCount(e.Location, e.Button);

        DispatchDown(contact, hit, e, isNew: true);
    }

    private void DispatchDown(PointerContact contact, UIElement? hit, PointerEventArgs e, bool isNew)
    {
        PointerContact? previousDispatch = _dispatching;
        _dispatching = contact;

        try
        {
            foreach (UIElement element in contact.Chain)
            {
                element.RaisePointerDown(e);
                if (e.Handled) return;
            }

            // арена собирается один раз на контакт, а не на кнопку
            if (isNew)
                contact.Arena = GestureArena.TryCreate(contact, CancelCompatInteraction);

            // арена до совместимых событий: распознаватель, которому хватает
            // самого нажатия, обязан успеть отменить их раньше, чем кнопка
            // покрасится нажатой
            contact.Arena?.PointerDown(e);

            if (contact.IsCompatCancelled) return;

            if (!contact.IsPrimary) return;

            var downArgs = new MouseButtonEventArgs(
                e.Button, MouseButtonState.Down, e.Location, e.Modifiers);

            foreach (UIElement element in contact.Chain)
                element.RaisePreviewMouseDown(downArgs);

            hit?.RaiseMouseDown(downArgs);

            if (e.Button == MouseButton.Left && hit is not null)
                _focusDispatcher.FocusElement(hit);
        }
        finally
        {
            _dispatching = previousDispatch;
        }
    }

    internal void OnPointerDown(Point point, MouseButton button = MouseButton.Left, KeyModifiers modifiers = KeyModifiers.None) =>
    OnPointerDown(new PointerEventArgs(
        MousePointerId, PointerKind.Mouse, point, button, 1f, modifiers));

    internal void OnPointerUp(Point point, MouseButton button = MouseButton.Left, KeyModifiers modifiers = KeyModifiers.None) =>
        OnPointerUp(new PointerEventArgs(
            MousePointerId, PointerKind.Mouse, point, button, 1f, modifiers));

    // ===== Конвейер контактов =====

    internal void OnPointerMove(PointerEventArgs e)
    {
        // положение курсора — понятие мыши: тултип и инспектор опираются
        // на него, и палец двигать его не должен
        if (e.Kind == PointerKind.Mouse)
            _lastPointerPosition = e.Location;

        _contacts.TryGetValue(e.PointerId, out PointerContact? contact);
        contact?.Update(e.Location, e.Timestamp);

        // при живом контакте цепочка строится от захватившего или нажатого,
        // а не от того, над кем курсор: иначе предпросмотр посыпался бы
        // в чужое поддерево
        UIElement? target = contact?.Target
            ?? (contact is null && e.Kind == PointerKind.Mouse ? HitTestAll(e.Location) : null);

        UIElement[] chain = BuildChain(target);

        // сохранить и вернуть, а не обнулить: RaiseMouseDown может открыть
        // модальный диалог, тот прокрутит вложенный цикл со своими контактами,
        // и обнуление в его finally оборвало бы наш кадр
        PointerContact? previousDispatch = _dispatching;
        _dispatching = contact;

        try
        {
            foreach (UIElement element in chain)
            {
                element.RaisePointerMove(e);
                if (e.Handled) return;
            }

            contact?.Arena?.PointerMove(e);

            // контакт достался жесту — совместимой части взаимодействия
            // больше нет, и наведение с курсором её не касаются
            if (contact is { IsCompatCancelled: true }) return;

            var moveArgs = new MouseMoveEventArgs(e.Location);

            // предпросмотр от корня к цели, до того как движение получит
            // она сама. Работает и с зажатой кнопкой — именно там он и нужен,
            // чтобы предок мог следить за перетаскиванием над своими потомками
            foreach (UIElement element in chain)
                element.RaisePreviewMouseMove(moveArgs);

            if (contact?.Target is UIElement held)
            {
                held.RaiseMouseMove(e.Location);
                return;
            }

            // дальше только наведение, а его не бывает у касания: палец
            // либо на экране, либо нет, состояния «над элементом» нет
            if (e.Kind != PointerKind.Mouse) return;

            UIElement? hit = target;

            if (hit != _hoveredElement)
            {
                // в аргументах указываем «откуда» и «куда», чтобы обработчик
                // мог отличить переход внутрь потомка от выхода наружу
                _hoveredElement?.RaiseMouseExit(e.Location, hit);
                hit?.RaiseMouseEnter(e.Location, _hoveredElement);

                _hoveredElement = hit;

                ScheduleToolTip(hit);
            }

            hit?.RaiseMouseMove(e.Location);

            CursorKind cursor = hit?.EffectiveCursor ?? CursorKind.Arrow;

            if (cursor != _lastCursor)
            {
                _lastCursor = cursor;
                PlatformWindow?.SetCursor(cursor);
            }

            if (IsInspectorEnabled)
            {
                InspectedElement = !IsInsideInspector(e.Location) && Content is not null
                    ? HitTester.HitTest(Content, e.Location)
                    : null;

                InvalidateVisual();
            }
        }
        finally
        {
            _dispatching = previousDispatch;
        }
    }

    internal void OnPointerLeaveWindow()
    {
        HideToolTip();
        _hoveredElement?.RaiseMouseExit(_lastPointerPosition, null);
        _hoveredElement = null;
    }

    internal void OnPointerUp(PointerEventArgs e)
    {
        if (e.Kind == PointerKind.Mouse)
            _lastPointerPosition = e.Location;

        // отпускание без нажатия — обычное дело после отмены: захват
        // отобрали, контакта уже нет, а система всё равно досылает Up
        if (!_contacts.TryGetValue(e.PointerId, out PointerContact? contact))
            return;

        contact.Update(e.Location, e.Timestamp);

        UIElement? hit = HitTestAll(e.Location);

        // сохранить и вернуть, а не обнулить: RaiseMouseDown может открыть
        // модальный диалог, тот прокрутит вложенный цикл со своими контактами,
        // и обнуление в его finally оборвало бы наш кадр
        PointerContact? previousDispatch = _dispatching;
        _dispatching = contact;

        try
        {
            foreach (UIElement element in contact.Chain)
            {
                element.RaisePointerUp(e);
                if (e.Handled) break;
            }

            contact.Arena?.PointerUp(e);

            if (contact.IsCompatCancelled) return;

            if (!contact.IsPrimary) return;

            var upArgs = new MouseButtonEventArgs(
                e.Button, MouseButtonState.Up, e.Location, e.Modifiers);

            if (e.Button == MouseButton.Left)
            {
                // цепочка та же, что при нажатии: кто следил за press через
                // предпросмотр, должен узнать и об отпускании
                for (UIElement? current = contact.Pressed; current is not null; current = current.Parent)
                    current.RaisePreviewMouseUp(upArgs);

                contact.Pressed?.RaiseMouseUp(upArgs);

                // захвативший должен узнать об отпускании, даже если нажатие
                // пришлось на его потомка
                if (contact.Capture is not null && !ReferenceEquals(contact.Capture, contact.Pressed))
                    contact.Capture.RaiseMouseUp(upArgs);

                ReleaseCapture(contact);

                // клик = нажатие и отпускание на одном элементе
                if (hit is not null && ReferenceEquals(hit, contact.Pressed))
                    BubbleClick(hit, e.Button, e.Location);
            }
            else
            {
                hit?.RaiseMouseUp(upArgs);

                // правая и средняя не требуют совпадения с нажатием:
                // захвата для них нет, поэтому клик по факту отпускания.
                // Поведение сохранено как было — на мой взгляд оно спорное
                // и просится в список багов первым пунктом
                if (hit is not null)
                    BubbleClick(hit, e.Button, e.Location);
            }
        }
        finally
        {
            _dispatching = previousDispatch;

            // завершение контакта — в finally, а не после try. Выходы выше
            // (жест забрал контакт, палец не ведущий, обработчик пометил
            // событие обработанным) обходили этот код стороной, и контакт
            // оставался в словаре навсегда: следующее движение мыши шло
            // не тому, кто под курсором, а покойнику из мёртвого контакта,
            // распознаватели так и не получали Leave, а захват не снимался.
            // Снаружи это выглядело как пропавшие клики и свайп через раз
            contact.Arena?.Complete();

            contact.Buttons &= ~e.Button.ToFlag();

            // у касания и пера кнопок нет — контакт кончается вместе с Up
            if (contact.Kind != PointerKind.Mouse || contact.Buttons == PointerButtons.None)
                EndContact(contact);
        }
    }

    /// <summary>Платформа сообщила, что контакт отменён: pointercancel
    /// в браузере, ACTION_CANCEL на Android, жест системной оболочки.</summary>
    internal void OnPointerCancel(int pointerId)
    {
        if (_contacts.TryGetValue(pointerId, out PointerContact? contact))
            CancelContact(contact, PointerCancelReason.Platform);
    }

    /// <summary>Цепочка от корня к элементу. Порядок именно такой:
    /// и предпросмотр, и pointer-события тоннелируют сверху вниз.</summary>
    private static UIElement[] BuildChain(UIElement? element)
    {
        if (element is null) return [];

        List<UIElement> chain = [];

        for (UIElement? current = element; current is not null; current = current.Parent)
            chain.Add(current);

        chain.Reverse();

        return [.. chain];
    }

    /// <summary>Оборвать совместимую часть взаимодействия, оставив контакт живым.
    /// Нужно победе жеста: кнопке под пальцем надо сообщить, что нажатия
    /// не было, а контакт продолжает идти — его ведёт распознаватель.</summary>
    private void CancelCompatInteraction(PointerContact contact, PointerCancelReason reason)
    {
        if (contact.IsCompatCancelled) return;

        contact.IsCompatCancelled = true;

        var args = new PointerCancelEventArgs(
            contact.Id, contact.Kind, contact.Location, reason);

        // отменяем всю цепочку, а не только цель: предок, следивший
        // за нажатием через предпросмотр, тоже завёл состояние по нему
        foreach (UIElement element in contact.Chain)
            element.RaisePointerCanceled(args);

        if (contact.Capture is not null && Array.IndexOf(contact.Chain, contact.Capture) < 0)
            contact.Capture.RaisePointerCanceled(args);

        ReleaseCapture(contact);

        // цель обнуляем, иначе движения продолжат уходить в отменённый элемент
        contact.Pressed = null;
    }

    private void CancelContact(PointerContact contact, PointerCancelReason reason)
    {
        contact.Arena?.Cancel();

        CancelCompatInteraction(contact, reason);

        EndContact(contact);
    }

    private void EndContact(PointerContact contact)
    {
        ReleaseCapture(contact);

        _contacts.Remove(contact.Id);

        if (_primaryContactId == contact.Id)
            _primaryContactId = null;
    }

    private void ReleaseCapture(PointerContact contact)
    {
        if (contact.Capture is null) return;

        contact.Capture = null;

        ZfContract.Require(_platformCaptureCount > 0,
            "Счётчик системного захвата ушёл в минус: захват отпустили дважды.");

        if (--_platformCaptureCount == 0)
            PlatformWindow?.ReleaseMouseCapture();
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
        // захват берут изнутри обработки контакта — DragList зовёт его
        // из OnPreviewMouseDown. Вне обработки берём ведущий контакт:
        // так работает старый код, звавший CaptureMouse из таймера
        PointerContact? contact = _dispatching ?? PrimaryContact;

        if (contact is null)
        {
            // до 0.11 захват брался безусловно, поэтому тихий отказ здесь —
            // это молча сломанное перетаскивание у того, кто позвал
            // CaptureMouse не из обработчика нажатия
            ZfContract.Require(false, "CaptureMouse вне живого контакта: захватывать нечего.");
            return;
        }

        if (ReferenceEquals(contact.Capture, element)) return;

        // захват означает, что элемент забрал указатель себе, — значит
        // борьба жестов на этом контакте кончена. Иначе распознаватель
        // предка спокойно дожидался отпускания и выигрывал уже захваченный
        // контакт: перетаскивание в DragList заканчивалось свайпом страницы,
        // а перетаскиваемая строка оставалась висеть, потому что отпускания
        // захвативший так и не получал.
        //
        // Но только пока борьба идёт. Если победитель уже есть, захват
        // берёт он сам — распознаватель прокрутки делает это сразу после
        // победы, — и отменять арену значило бы снимать его же с контакта
        if (contact.Arena is { HasWinner: false } arena)
        {
            arena.Cancel();
            contact.Arena = null;
        }

        if (contact.Capture is null && _platformCaptureCount++ == 0)
            PlatformWindow?.CaptureMouse();

        contact.Capture = element;
    }

    internal void ReleaseMouseCapture(UIElement element)
    {
        foreach (PointerContact contact in _contacts.Values)
        {
            if (!ReferenceEquals(contact.Capture, element)) continue;

            ReleaseCapture(contact);
            return;
        }
    }

    /// <summary>Захват отобрала система.</summary>
    /// <remarks>
    /// Раньше здесь рассылался поддельный MouseUp. Кнопке всё равно —
    /// Click всплывает отдельно, из BubbleClick. А TrackBar и GridSplitter
    /// принимали его за настоящее отпускание и фиксировали значение,
    /// хотя взаимодействие оборвали.
    /// </remarks>
    internal void OnCaptureLost()
    {
        // ToArray: CancelContact правит словарь под нами
        foreach (PointerContact contact in _contacts.Values.ToArray())
            CancelContact(contact, PointerCancelReason.CaptureLost);

        // захвата в системе уже нет, счётчик сводим к нулю без вызова
        // ReleaseMouseCapture — отпускать нечего
        _platformCaptureCount = 0;
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

        // тема меняет и Font.Default, а он влияет на размер всего, что
        // не задало шрифт само. Через свойства такой элемент не проходит,
        // поэтому кэш измерения сбрасываем по всему дереву
        InvalidateMeasureTree();

        Invalidate();
    }

    /// <summary>Оформить поддерево по текущей теме. Вызывается при
    /// присоединении к форме и при смене темы.</summary>
    internal void ApplyTheme(UIElement root)
    {
        Walk(root, App.Theme.Apply);
    }

    /// <summary>Сбросить кэш измерения по всему дереву. Нужно там, где
    /// размеры зависят не от свойств элементов, а от чего-то общего:
    /// базового шрифта, масштаба, измерителя текста.</summary>
    internal void InvalidateMeasureTree()
    {
        if (Content is not null)
            Walk(Content, static element => element.InvalidateMeasure());

        foreach (UIElement overlay in _overlays.ToArray())
            Walk(overlay, static element => element.InvalidateMeasure());
    }

    /// <summary>Сменить тему с расходящейся волной от точки.</summary>
    public void SwitchTheme(Theme theme, Point origin)
    {
        // волна — украшение: при уменьшенном движении тема меняется сразу
        if (Motion.IsReduced)
        {
            App.Theme = theme;
            return;
        }

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

        // контакт, чья цель уехала из дерева, обязан узнать об отмене.
        // Раньше поля здесь просто занулялись, и элемент, если его потом
        // вернули в дерево, оставался нажатым
        foreach (PointerContact contact in _contacts.Values.ToArray())
        {
            bool affected =
                (contact.Pressed is not null && IsInTree(root, contact.Pressed)) ||
                (contact.Capture is not null && IsInTree(root, contact.Capture));

            if (affected)
                CancelContact(contact, PointerCancelReason.Detached);
        }

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

        CancelAnimationsIn(root);
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

    /// <summary>Раскладка устарела и ждёт ближайшего кадра.</summary>
    private bool _layoutDirty;

    /// <summary>Сколько проходов допускается за один вызов.</summary>
    /// <remarks>
    /// Двух хватает штатному случаю: первый создаёт контейнеры, второй
    /// считает по ним итоговый размер. Запас до четырёх — на вложенные
    /// виртуализующие панели, дерево внутри прокручиваемой панели.
    /// </remarks>
    private const int MaxLayoutPasses = 4;

    internal void PerformLayout()
    {
        // Invalidate во время раскладки — законное явление: виртуализующая
        // панель создаёт контейнеры прямо в измерении, потому что до него
        // неизвестно, сколько строк влезет, а создание контейнера меняет
        // Children. Заново заходить в проход нельзя, а просьба уже записана
        // в _layoutDirty — её выполнит цикл проходов ниже
        if (_layoutDepth > 0) return;

        _layoutDepth++;

        try
        {
            for (int pass = 0; ; pass++)
            {
                _layoutDirty = false;

                LayoutPass();

                if (!_layoutDirty) break;

                if (pass + 1 >= MaxLayoutPasses)
                {
                    // раскладка не сходится: кто-то просит новый проход
                    // каждый раз. Молча крутить это значит повесить кадр
                    ZfContract.Fail(
                        $"Раскладка не сошлась за {MaxLayoutPasses} проходов: " +
                        "кто-то продолжает звать Invalidate из MeasureOverride " +
                        "или ArrangeOverride. Изменяйте там геометрию напрямую " +
                        "вместо запроса нового прохода.");

                    break;
                }
            }
        }
        finally
        {
            _layoutDepth--;
            _layoutDirty = false;
        }
    }

    /// <summary>Выполнить отложенную раскладку, если она нужна. Зовётся
    /// перед отрисовкой и перед попаданием — то есть везде, где геометрия
    /// должна быть свежей.</summary>
    internal void EnsureLayout()
    {
        if (!_layoutDirty) return;

        PerformLayout();
    }

    /// <summary>Досчитать отложенную раскладку немедленно. Нужно там, где
    /// код сразу после изменения читает геометрию: позицию, размер, состав
    /// контейнеров виртуализующей панели.</summary>
    public void UpdateLayout() => EnsureLayout();

    private void LayoutPass()
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

    /// <summary>Пометить раскладку устаревшей и попросить кадр.</summary>
    /// <remarks>
    /// Раньше здесь же шёл полный проход Measure/Arrange по всему дереву.
    /// Тогда установка пяти свойств давала пять раскладок, а прокрутка
    /// пальцем — по раскладке на каждое событие движения, которых
    /// приходит больше, чем кадров. Теперь проход один и выполняется
    /// перед кадром: до отрисовки и до попадания геометрия всё равно
    /// будет свежей, а промежуточные состояния никто не увидит.
    /// Коду, которому геометрия нужна сразу, — UpdateLayout.
    /// </remarks>
    internal void Invalidate()
    {
        _layoutDirty = true;

        // видимость элемента — свойство раскладки, поэтому её смена всегда
        // проходит здесь. Часы пересматривают, нужны ли кадры: анимация
        // на показавшейся заново странице должна ожить, а на спрятанной —
        // перестать будить окно
        ReviewFrames();

        // полная перерисовка: копим всю клиентскую область
        _dirtyRegion = new Rectangle(Point.Empty, ClientSize);
        PlatformWindow?.Invalidate(null);
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

        _clock?.Stop();

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

        Schedule(durationMs, () =>
        {
            DetachOverlay(toast);
            _toasts.Remove(toast);
            ArrangeToasts(position);   // оставшиеся подтягиваются на освободившееся место
            Invalidate();
        });
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

        if (_toolTipOwner is not null)
            _toolTipWake = Schedule(ToolTipDelay, ShowToolTipCore);
    }

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
        _toolTipWake?.Dispose(); 
        _toolTipWake = null;

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
        // попадание считается по геометрии: если раскладка ждёт кадра,
        // клик попал бы в то, что было на экране до последнего изменения
        EnsureLayout();

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
        EnsureLayout();

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
        _toolTipWake?.Dispose(); 
        _clock?.Dispose();

        _focusDispatcher.FocusChanged -= OnFocusChangedForKeyboard;

        // контакт держит Chain — весь путь от корня до нажатого элемента.
        // Форму могли закрыть посреди перетаскивания, и тогда отпускание
        // не придёт никогда
        _contacts.Clear();
        _primaryContactId = null;
        _platformCaptureCount = 0;
    }
}