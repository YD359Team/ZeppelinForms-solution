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

    public UIElement? Content
    {
        get;
        set
        {
            if (field is not null) field.Owner = null;
            field = value;
            if (value is not null) value.Owner = this;
        }
    }

    public Size ClientSize { get; internal set; }

    // Второй, независимый корень рендера — flyout'ы/тултипы/попапы.
    // Рисуются поверх Content, без клипа предков, в оконных координатах.
    private readonly List<UIElement> _overlays = [];
    public IReadOnlyList<UIElement> Overlays => _overlays;

    private UIElement? _hoveredElement;
    private UIElement? _pressedElement;
    private readonly FocusDispatcher _focusDispatcher = new();

    public void Show()
    {
        PlatformWindow?.Show();
        Shown?.Invoke(this, EventArgs.Empty);
    }

    public void Close() => PlatformWindow?.Close();

    public void Invoke(Action action) => PlatformWindow?.Invoke(action);

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
        Invalidate();
    }

    // ===== Диспетчинг ввода =====

    private UIElement? HitTestAll(Point point)
    {
        // сверху вниз — последний добавленный overlay визуально выше остальных
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
        UIElement? hit = HitTestAll(point);

        if (hit == _hoveredElement)
            return;

        _hoveredElement?.RaiseMouseLeave();
        hit?.RaiseMouseOver();
        _hoveredElement = hit;
    }

    internal void OnPointerLeaveWindow()
    {
        _hoveredElement?.RaiseMouseLeave();
        _hoveredElement = null;
    }

    internal void OnPointerDown(Point point)
    {
        // клик мимо всех открытых flyout'ов — закрываем их и на этом гасим жест,
        // не пробрасывая клик дальше на Content под ними
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
