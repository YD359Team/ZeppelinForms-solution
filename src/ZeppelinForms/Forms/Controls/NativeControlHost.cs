using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// A native control of the platform among the form's elements: a Win32 window, an X11
/// window, an element of the page — a video player, a map SDK's view, a browser
/// engine, a legacy control nobody will rewrite. The platform packages derive the hosts
/// for their own handles: <c>HwndHost</c> on Windows, <c>X11WindowHost</c> on Linux,
/// <c>HtmlElementHost</c> in the browser.
/// </summary>
/// <remarks>
/// <para>
/// The host is laid out like any element and the control follows it: its place, its
/// size, the scrolling of a panel round it — the part the form doesn't show is cut off —
/// and its visibility, a hidden page's included. The control is made when the host is
/// first drawn in a window that can hold one (<see cref="INativeControlHostWindow"/>) and
/// destroyed when the host leaves the form or the window closes; a host shown again
/// makes a new one.
/// </para>
/// <para>
/// A native control is a window of its own above the form's picture, as WPF's and
/// WinForms' are: the form's popups, flyouts and toasts open under it, and it ignores
/// the form's opacity, transforms and effects. The keyboard is its own while it has the
/// focus: Tab moves the focus into it, not out of it.
/// </para>
/// <para>
/// Where the window can't hold native controls — Wayland and Android for now, a
/// headless test without the support — the host draws a placeholder instead.
/// </para>
/// </remarks>
public abstract class NativeControlHost : UnitControl, IInputElement
{
    private Form? _form;
    private INativeControlSlot? _slot;

    /// <summary>The window the slot was made in: a form shown in a new window makes the
    /// control again.</summary>
    private IPlatformWindow? _slotWindow;

    private (Rectangle Bounds, Rectangle Visible) _placed;

    // the slot reported the control took the keyboard: the form's focus moves to the
    // host without handing the keyboard back to the control
    private bool _focusFromControl;

    protected NativeControlHost()
    {
        // a view onto content of its own: it fills the room it is given, see UnitControl
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        SetControlDefault(VerticalAlignmentProperty, VerticalAlignment.Stretch);
    }

    /// <summary>The native control, once made; null before it is and after it is destroyed.</summary>
    public PlatformHandle? NativeControl { get; private set; }

    /// <summary>The native control was made: the moment to give its handle to whatever
    /// draws into it — a player told to render into the window, say.</summary>
    public event EventHandler? NativeControlCreated;

    /// <summary>The native control is about to be destroyed: the moment to stop what
    /// draws into it.</summary>
    public event EventHandler? NativeControlDestroying;

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    /// <summary>Make the native control in <paramref name="parent"/>, the slot the window
    /// gave, and return its handle. Called once per window, on the UI thread.</summary>
    protected abstract PlatformHandle CreateNativeControlCore(PlatformHandle parent);

    /// <summary>Destroy the control <see cref="CreateNativeControlCore"/> made. The default
    /// destroys it as the platform destroys its windows.</summary>
    protected virtual void DestroyNativeControlCore(PlatformHandle control) => _slot?.DestroyControl(control);

    // ===== life =====

    protected override void OnAttached()
    {
        base.OnAttached();

        _form = FindOwner();
        _form?.RegisterNativeHost(this);
    }

    protected override void OnDetached()
    {
        base.OnDetached();

        _form?.UnregisterNativeHost(this);
        _form = null;

        ReleaseNativeControl();
    }

    /// <summary>The control and its slot go: the host left the form, or the form's window
    /// closed.</summary>
    internal void ReleaseNativeControl()
    {
        if (_slot is not { } slot) return;

        if (NativeControl is { } control)
        {
            NativeControlDestroying?.Invoke(this, EventArgs.Empty);

            // a control that is gone already — its window destroyed with the form's —
            // is no reason to keep the slot
            try
            {
                DestroyNativeControlCore(control);
            }
            finally
            {
                NativeControl = null;
            }
        }

        slot.ControlFocused -= OnControlFocused;
        slot.Dispose();

        _slot = null;
        _slotWindow = null;
        _placed = default;

        Invalidate();
    }

    /// <summary>Follow the host with the control: called by the form after every frame,
    /// when every element is where the frame drew it.</summary>
    internal void UpdatePlacement(Form form)
    {
        IPlatformWindow? window = form.PlatformWindow;

        // a new window — the form closed and shown again — makes the control anew
        if (_slot is not null && !ReferenceEquals(window, _slotWindow))
            ReleaseNativeControl();

        bool shown = IsEffectivelyVisible && ActualSize.Width > 0 && ActualSize.Height > 0;

        if (_slot is null)
        {
            if (!shown || window is not INativeControlHostWindow host || host.CreateNativeSlot() is not { } slot)
                return;

            _slot = slot;
            _slotWindow = window;
            slot.ControlFocused += OnControlFocused;

            PlatformHandle control = CreateNativeControlCore(slot.Parent);
            NativeControl = control;
            slot.Attach(control);

            // the placeholder gives way to the control
            Invalidate();

            NativeControlCreated?.Invoke(this, EventArgs.Empty);
        }

        (Rectangle bounds, Rectangle visible) = shown ? Placement(form) : (_placed.Bounds, Rectangle.Empty);

        if ((bounds, visible) == _placed) return;

        _placed = (bounds, visible);
        _slot.Place(bounds, visible);
    }

    /// <summary>Where the host is in the form's client area, and the part of it the
    /// form shows: cut by every ancestor that cuts its children — a scrolled panel's
    /// viewport, a border's inside — and by the client area.</summary>
    private (Rectangle Bounds, Rectangle Visible) Placement(Form form)
    {
        var bounds = new Rectangle(GetAbsolutePosition(), ActualSize);
        Rectangle visible = bounds.Intersect(new Rectangle(Point.Empty, form.ClientSize));

        for (UIElement? ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            Point origin = ancestor.GetAbsolutePosition();
            visible = visible.Intersect(ancestor.ClipBounds.Offset(origin.X, origin.Y));
        }

        // in logical units as laid out: the platform rounds the edges to its pixels
        return (bounds, visible.Width > 0 && visible.Height > 0 ? visible : Rectangle.Empty);
    }

    // ===== focus =====

    private void OnControlFocused(object? sender, EventArgs e)
    {
        if (IsFocused) return;

        _focusFromControl = true;

        try
        {
            _form?.FocusElement(this);
        }
        finally
        {
            _focusFromControl = false;
        }
    }

    protected override void OnGotFocus()
    {
        base.OnGotFocus();

        // the form's focus came here by a Tab or from code: the keyboard goes on to
        // the control. Not when the control took it itself — it has it already
        if (!_focusFromControl) _slot?.Focus();
    }

    // ===== drawing =====

    /// <summary>The control covers the host; without one — on a platform that can't
    /// hold it — the host shows where it would be.</summary>
    public override void Draw(Graphics g)
    {
        if (NativeControl is not null) return;

        DrawPlaceholder(g);
    }

    /// <summary>What stands in the control's place where the window can't hold one: a
    /// quiet crossed-out frame. Override it for a picture of one's own.</summary>
    protected virtual void DrawPlaceholder(Graphics g)
    {
        Rectangle bounds = LocalBounds;
        Color line = new(90, 128, 128, 128);

        g.FillRectangle(bounds, new Color(18, 128, 128, 128));
        g.DrawRectangle(bounds, line, 1f);
        g.DrawLine(bounds.Position, new Point(bounds.Right, bounds.Bottom), line, 1f);
        g.DrawLine(new Point(bounds.Right, bounds.Y), new Point(bounds.X, bounds.Bottom), line, 1f);
    }
}