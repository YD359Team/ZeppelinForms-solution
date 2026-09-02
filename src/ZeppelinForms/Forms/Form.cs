using System.Diagnostics;
using ZeppelinForms.Animation;
using ZeppelinForms.Core.Text;
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
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms;

public class Form : IDisposable
{
    /// <summary>Флаут закрыт — по клику мимо, программно или вместе с формой.
    /// Контролы, открывшие его, обязаны сбросить свою ссылку здесь.</summary>
    public event EventHandler<UIElement>? FlyoutClosed;
    public event EventHandler? Shown;

    internal IPlatformWindow? PlatformWindow { get; set; }

    public WindowStartupLocation WindowStartupLocation { get; set; }

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
            PlatformWindow?.SetOpacity(_opacity);
        }
    }

    public string Name { get; set; } = string.Empty;
    public string? Title { get; set; }
    public Icon? Icon { get; set; }
    public Point Position { get; set; }
    public Size Size { get; set; }

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
            PlatformWindow?.SetWindowState(value);
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

    private readonly List<UIElement> _overlays = [];
    public IReadOnlyList<UIElement> Overlays => _overlays;
    private readonly List<UIElement> _flyouts = [];
    private readonly List<UIElement> _toasts = [];

    private UIElement? _hoveredElement;
    private UIElement? _pressedElement;
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

        if (_pressedElement is not null)
        {
            _pressedElement.RaiseMouseMove(point);
            return;
        }

        UIElement? hit = HitTestAll(point);

        if (hit != _hoveredElement)
        {
            // в аргументах указываем «откуда» и «куда», чтобы обработчик
            // мог отличить переход внутрь потомка от выхода наружу
            _hoveredElement?.RaiseMouseExit(point, hit);
            hit?.RaiseMouseEnter(point, _hoveredElement);

            _hoveredElement = hit;

            ScheduleToolTip(hit);
            PlatformWindow?.SetCursor(hit?.EffectiveCursor ?? CursorKind.Arrow);
        }

        hit?.RaiseMouseMove(point);

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

        if (hit is not null)
        {
            List<UIElement> chain = [];

            for (UIElement? current = hit; current is not null; current = current.Parent)
                chain.Add(current);

            chain.Reverse();

            foreach (UIElement element in chain)
                element.RaisePreviewMouseDown(point);
        }

        if (button == MouseButton.Left)
            _pressedElement = hit;

        hit?.RaiseMouseDown(new MouseButtonEventArgs(button, MouseButtonState.Down, point, modifiers));

        if (button == MouseButton.Left && hit is not null)
            _focusDispatcher.FocusElement(hit);
    }

    internal void OnPointerUp(Point point, MouseButton button = MouseButton.Left, KeyModifiers modifiers = KeyModifiers.None)
    {
        UIElement? hit = HitTestAll(point);

        var upArgs = new MouseButtonEventArgs(button, MouseButtonState.Up, point, modifiers);

        if (button == MouseButton.Left)
        {
            _pressedElement?.RaiseMouseUp(upArgs);

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

    internal void OnKeyDown(Key key, KeyModifiers modifiers)
    {
        if (key == Key.F12 || (key == (Key)0x49 && modifiers.HasFlag(KeyModifiers.Control) && modifiers.HasFlag(KeyModifiers.Shift)))
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
        var args = new KeyEventArgs(key, modifiers);

        for (UIElement? current = _focusDispatcher.FocusedElement; current is not null; current = current.Parent)
        {
            current.RaiseKeyUp(args);
            if (args.Handled) break;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (Content is null) return;

        ApplyTheme(Content);
        Invalidate();
    }

    /// <summary>Оформить поддерево по текущей теме. Вызывается при
    /// присоединении к форме и при смене темы.</summary>
    internal void ApplyTheme(UIElement root)
    {
        Walk(root, App.Theme.Apply);
    }

    public void Show()
    {
        PlatformWindow?.SetOpacity(_opacity);
        PlatformWindow?.Show();
        Shown?.Invoke(this, EventArgs.Empty);
    }

    public void Close() => PlatformWindow?.Close();

    public void Invoke(Action action) => PlatformWindow?.Invoke(action);

    public UIElement? FindByName(string name) => NameScope.Find(name);
    public T? FindByName<T>(string name) where T : UIElement => NameScope.Find<T>(name);

    // ===== Присоединение поддерева к форме =====

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

        InspectedElement = null;
        _toolTipOwner = null;

        _animations.RemoveAll(a => a.Target is UIElement e && IsInTree(root, e));
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
            System.Diagnostics.Debug.WriteLine("PerformLayout вызван повторно во время раскладки");
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
        // одна анимация на связку «объект + свойство»
        _animations.RemoveAll(a => ReferenceEquals(a.Target, animation.Target) && a.Key == animation.Key);
        _animations.Add(animation);

        if (_animations.Count == 1)
        {
            _lastTickTicks = Environment.TickCount64;
            PlatformWindow?.StartTicking(FrameIntervalMs);
        }
    }

    internal void Tick()
    {
        long now = Environment.TickCount64;
        var elapsed = TimeSpan.FromMilliseconds(now - _lastTickTicks);
        _lastTickTicks = now;

        for (int i = _animations.Count - 1; i >= 0; i--)
            if (!_animations[i].Advance(elapsed))
                _animations.RemoveAt(i);

        if (_animations.Count == 0)
            PlatformWindow?.StopTicking();

        InvalidateVisual();
    }

    // ===== Flyout API =====

    public void ShowFlyout(UIElement anchor, UIElement content, FlyoutPlacement placement = FlyoutPlacement.Bottom)
    {
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

        content.Owner = this;
        _overlays.Add(content);
        _flyouts.Add(content);

        Invalidate();
    }

    public void CloseFlyout(UIElement content)
    {
        if (!_overlays.Remove(content)) return;

        _flyouts.Remove(content);
        content.Owner = null;

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
        {
            _overlays.Remove(flyout);
            flyout.Owner = null;
        }

        _flyouts.Clear();

        foreach (UIElement flyout in closing)
            FlyoutClosed?.Invoke(this, flyout);

        Invalidate();
    }

    // ==== Dialog ====

    public DialogResult<T> ShowDialog<T>(Form owner)
    {
        IsDialog = true;

        IPlatform platform = owner.Platform
            ?? throw new InvalidOperationException("Владелец диалога ещё не привязан к платформе.");

        Platform = platform;
        platform.CreateWindow(this);

        _dialogAccepted = false;
        _dialogValue = null;

        Show();
        platform.RunModal(PlatformWindow!, owner.PlatformWindow);

        return _dialogAccepted && _dialogValue is T typed
            ? new DialogResult<T>(true, typed)
            : DialogResult<T>.Cancelled();
    }

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

        toast.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
        toast.Owner = this;

        _overlays.Add(toast);
        _toasts.Add(toast);
        ArrangeToasts(position);
        Invalidate();

        System.Threading.Timer? timer = null;
        timer = new System.Threading.Timer(_ =>
        {
            Invoke(() =>
            {
                _overlays.Remove(toast);
                _toasts.Remove(toast);
                toast.Owner = null;
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

        _activeToolTip = tip;
        _overlays.Add(tip);

        Invalidate();
    }

    private void HideToolTip()
    {
        _toolTipTimer.Change(Timeout.Infinite, Timeout.Infinite);

        if (_activeToolTip is not null)
        {
            _overlays.Remove(_activeToolTip);
            _activeToolTip.Owner = null;
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

            _inspectorGrid.Owner = this;
            _overlays.Add(_inspectorGrid);
        }
        else if (_inspectorGrid is not null)
        {
            _overlays.Remove(_inspectorGrid);
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

    internal void OnMouseWheel(Point point, int delta)
    {
        UIElement? hit = HitTestAll(point);
        if (hit is null) return;

        var args = new MouseWheelEventArgs(point, delta);

        for (UIElement? current = hit; current is not null; current = current.Parent)
        {
            current.RaiseMouseWheel(args);
            if (args.Handled)
                break;
        }
    }

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

        menu.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

        float x = Math.Min(position.X, Math.Max(0, ClientSize.Width - menu.DesiredSize.Width));
        float y = Math.Min(position.Y, Math.Max(0, ClientSize.Height - menu.DesiredSize.Height));

        menu.Position = new Point(x, y);
        menu.Owner = this;

        _overlays.Add(menu);
        _flyouts.Add(menu);   // закроется кликом мимо — как и положено меню

        Invalidate();
    }

    public void Dispose()
    {
        App.ThemeChanged -= OnThemeChanged;
        _toolTipTimer?.Dispose();
    }
}
