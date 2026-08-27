using System.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Dispatchers;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms;

public class Form
{
    internal IPlatformWindow? PlatformWindow { get; set; }

    public WindowStartupLocation WindowStartupLocation { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Title { get; set; }
    public Icon? Icon { get; set; }
    public Point Position { get; set; }
    public Size Size { get; set; }

    public event EventHandler? Shown;

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

    public Form()
    {
        _toolTipTimer = new System.Threading.Timer(
            OnToolTipTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Show()
    {
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

        Invalidate();
    }

    public void CloseFlyout(UIElement content)
    {
        if (_overlays.Remove(content))
        {
            content.Owner = null;
            Invalidate();
        }
    }

    public void CloseAllFlyouts()
    {
        if (_overlays.Count == 0) return;

        foreach (var overlay in _overlays)
            overlay.Owner = null;

        _overlays.Clear();
        _activeToolTip = null;
        Invalidate();
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

    private bool IsInsideAnyOverlay(Point point)
    {
        foreach (var overlay in _overlays)
            if (HitTester.HitTest(overlay, point) is not null)
                return true;

        return false;
    }

    internal void OnPointerMove(Point point)
    {
        _lastPointerPosition = point;

        UIElement? hit = HitTestAll(point);

        if (hit == _hoveredElement)
            return;

        _hoveredElement?.RaiseMouseLeave();
        hit?.RaiseMouseOver();
        _hoveredElement = hit;

        ScheduleToolTip(hit);

        if (IsInspectorEnabled)
            Invalidate();
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

        if (_overlays.Count > 0 && !IsInsideAnyOverlay(point))
        {
            CloseAllFlyouts();
            return;
        }

        UIElement? hit = HitTestAll(point);

        if (hit is { IsEnabled: false }) return;

        _pressedElement = hit;
        hit?.RaiseMouseDown();

        if (hit is not null)
            _focusDispatcher.FocusElement(hit);
    }

    internal void OnPointerUp(Point point)
    {
        UIElement? hit = HitTestAll(point);

        _pressedElement?.RaiseMouseUp();

        if (hit is not null && ReferenceEquals(hit, _pressedElement))
            hit.RaiseClick(MouseButton.Left, point);

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
}
