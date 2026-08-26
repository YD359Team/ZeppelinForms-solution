using System.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Dispatchers;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms;

public class Form
{
    public event EventHandler? Shown;

    internal IPlatformWindow? PlatformWindow { get; set; }

    public WindowStartupLocation WindowStartupLocation { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Title { get; set; }
    public Icon? Icon { get; set; }
    public Point Position { get; set; }
    public Size Size { get; set; }

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
        if (Content is null) return;
        Content.Measure(ClientSize);
        Content.Arrange(new Rectangle(Point.Empty, ClientSize));
    }

    internal void Invalidate()
    {
        PerformLayout();
        PlatformWindow?.Invalidate();
    }

    internal void OnPointerMove(Point point)
    {
        UIElement? hit = Content is not null ? HitTester.HitTest(Content, point) : null;

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
        UIElement? hit = Content is not null ? HitTester.HitTest(Content, point) : null;

        if (hit is { IsEnabled: false }) return;

        _pressedElement = hit;
        hit?.RaiseMouseDown();

        if (hit is not null)
            _focusDispatcher.FocusElement(hit);
    }

    internal void OnPointerUp(Point point)
    {
        UIElement? hit = Content is not null ? HitTester.HitTest(Content, point) : null;

        _pressedElement?.RaiseMouseUp();

        // клик = mouse down и mouse up на ОДНОМ И ТОМ ЖЕ элементе,
        // а не просто "отпустили кнопку где-то"
        if (hit is not null && ReferenceEquals(hit, _pressedElement))
            hit.RaiseClick(MouseButton.Left, point);

        _pressedElement = null;
    }

    internal void OnMouseWheel(Point point, int delta)
    {
        UIElement? hit = Content is not null ? HitTester.HitTest(Content, point) : null;
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
