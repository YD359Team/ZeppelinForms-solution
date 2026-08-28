using System.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
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
    public event EventHandler? Shown;

    internal IPlatformWindow? PlatformWindow { get; set; }

    public WindowStartupLocation WindowStartupLocation { get; set; }

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
        }
    }

    public Size ClientSize { get; internal set; }

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
    public UIElement? InspectedElement => IsInspectorEnabled ? _hoveredElement : null;

    private bool _dialogAccepted;
    private object? _dialogValue;

    internal IPlatform? Platform { get; set; }

    public Form()
    {
        _toolTipTimer = new System.Threading.Timer(
            OnToolTipTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
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
            element.RaiseAttached();
        });
    }

    internal void DetachTree(UIElement root) => Walk(root, NameScope.Unregister);

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

    internal void PerformLayout()
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

    internal void InvalidateVisual() => PlatformWindow?.Invalidate();

    internal void Invalidate()
    {
        PerformLayout();
        PlatformWindow?.Invalidate();
    }

    // ===== Flyout API =====

    public void ShowFlyout(UIElement anchor, UIElement content, FlyoutPlacement placement = FlyoutPlacement.Bottom)
    {
        content.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

        Point anchorPos = anchor.GetAbsolutePosition();
        Size anchorSize = anchor.Size;
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
        if (_overlays.Remove(content))
        {
            _flyouts.Remove(content);
            content.Owner = null;
            Invalidate();
        }
    }

    public void CloseAllFlyouts()
    {
        if (_flyouts.Count == 0) return;

        foreach (var flyout in _flyouts)
        {
            _overlays.Remove(flyout);
            flyout.Owner = null;
        }

        _flyouts.Clear();
        Invalidate();
    }

    // ==== Dialog ====

    public DialogResult<T> ShowDialog<T>(Form owner)
    {
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

    internal void OnKeyDown(Key key)
    {
        if (key == Key.F12)
        {
            IsInspectorEnabled = !IsInspectorEnabled;
            Invalidate();
            return;
        }

        var args = new KeyEventArgs(key);

        for (UIElement? current = _focusDispatcher.FocusedElement; current is not null; current = current.Parent)
        {
            current.RaiseKeyDown(args);
            if (args.Handled)
                break;
        }
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

    internal void OnKeyDown(Key key, KeyModifiers modifiers)
    {
        if (key == Key.F12)
        {
            IsInspectorEnabled = !IsInspectorEnabled;
            Invalidate();
            return;
        }

        var args = new KeyEventArgs(key, modifiers);

        for (UIElement? current = _focusDispatcher.FocusedElement; current is not null; current = current.Parent)
        {
            current.RaiseKeyDown(args);
            if (args.Handled)
                break;
        }

        // Tab обрабатываем последним: если сфокусированный контрол сам
        // хочет Tab (TextBox с IsTabAccepted), он уже выставил Handled
        if (!args.Handled && key == Key.Tab && Content is not null)
        {
            if (modifiers.HasFlag(KeyModifiers.Shift))
                _focusDispatcher.MovePrevious(Content);
            else
                _focusDispatcher.MoveNext(Content);
        }
    }

    internal void OnPointerMove(Point point)
    {
        _lastPointerPosition = point;

        // пока кнопка зажата — все move уходят элементу, который её поймал,
        // даже если курсор ушёл за его границы (мышиный захват)
        if (_pressedElement is not null)
        {
            _pressedElement.RaiseMouseMove(point);
            return;
        }

        UIElement? hit = HitTestAll(point);
        if (hit == _hoveredElement) return;

        _hoveredElement?.RaiseMouseLeave();
        hit?.RaiseMouseOver();
        _hoveredElement = hit;

        ScheduleToolTip(hit);

        if (IsInspectorEnabled) Invalidate();
    }

    internal void OnPointerLeaveWindow()
    {
        HideToolTip();
        _hoveredElement?.RaiseMouseLeave();
        _hoveredElement = null;
    }

    internal void OnPointerDown(Point point)
    {
        HideToolTip();

        // только флауты перехватывают клик мимо себя; тосты и тултипы — нет
        if (_flyouts.Count > 0 && !IsInsideAnyFlyout(point))
        {
            CloseAllFlyouts();
            return;
        }

        UIElement? hit = HitTestAll(point);
        if (hit is { IsEnabled: false }) return;

        _pressedElement = hit;
        hit?.RaiseMouseDown(point);

        if (hit is not null)
            _focusDispatcher.FocusElement(hit);
    }

    internal void OnPointerUp(Point point)
    {
        UIElement? hit = HitTestAll(point);

        _pressedElement?.RaiseMouseUp(point);

        if (hit is not null && ReferenceEquals(hit, _pressedElement))
        {
            var args = new MouseClickEventArgs(MouseButton.Left, MouseButtonState.Up, point);

            for (UIElement? current = hit; current is not null; current = current.Parent)
            {
                current.RaiseClick(args);
                if (args.Handled)
                    break;
            }
        }

        _pressedElement = null;
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

    public void Dispose()
    {
        _toolTipTimer?.Dispose();
    }
}
