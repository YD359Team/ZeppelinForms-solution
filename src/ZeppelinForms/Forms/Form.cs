using System.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Dispatchers;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms;

public class Form
{
    internal IPlatformWindow? PlatformWindow { get; set; }

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

    private UIElement? _hoveredElement;
    private UIElement? _pressedElement;
    private readonly FocusDispatcher _focusDispatcher = new();


    public void Show()
    {
        PlatformWindow?.Show();
    }

    internal void Invalidate()
    {
        Debug.WriteLine("Form.Invalidate");
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

        _pressedElement = hit;
        hit?.RaiseMouseDown();

        if (hit is IInputElement input)
            _focusDispatcher.FocusElement(input);
    }

    internal void OnPointerUp(Point point)
    {
        _pressedElement?.RaiseMouseUp();
        _pressedElement = null;
    }
}
