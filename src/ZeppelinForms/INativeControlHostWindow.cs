using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms;

/// <summary>A handle of the platform's own: a window, an X11 window, a page element —
/// what a native control is and what it is made a child of.</summary>
/// <param name="Handle">The handle itself: an HWND, an XID, or the number the browser
/// platform keeps an element under.</param>
/// <param name="Kind">What the handle is: <see cref="Hwnd"/>, <see cref="Xid"/>,
/// <see cref="HtmlElement"/> — a host checks it before taking the handle for its own.</param>
public readonly record struct PlatformHandle(nint Handle, string Kind)
{
    /// <summary>A Win32 window.</summary>
    public const string Hwnd = "HWND";

    /// <summary>An X11 window.</summary>
    public const string Xid = "XID";

    /// <summary>An element of the page, kept by the browser platform under the number.</summary>
    public const string HtmlElement = "HTMLElement";
}

/// <summary>A window that can hold native controls among the form's elements:
/// <c>NativeControlHost</c> asks it for a slot. Windows and X11 windows can, and the
/// browser's canvas; a Wayland window can't yet — there a host draws its placeholder.</summary>
public interface INativeControlHostWindow
{
    /// <summary>A new place for one native control, empty and hidden until placed; null
    /// where this window can't hold one after all — a host then draws its placeholder.</summary>
    INativeControlSlot? CreateNativeSlot();
}

/// <summary>
/// The place of one native control in a window: a child window of the form's own —
/// a <c>div</c> over the canvas in the browser — that cuts the control to the part of
/// it the form shows.
/// </summary>
/// <remarks>
/// The slot is as large as the visible part of the host, and the control inside is as
/// large as the host and offset by what is cut off: a control in a scrolled list is
/// clipped at the list's edge, as the form's own elements are. A native control is
/// always above what the form draws — its menus and flyouts open under it.
/// </remarks>
public interface INativeControlSlot : IDisposable
{
    /// <summary>What the native control is created in: a host passes it to the code that
    /// builds the control, as WPF's HwndHost passes BuildWindowCore its parent.</summary>
    PlatformHandle Parent { get; }

    /// <summary>The control the host built: made the slot's child if it isn't one yet,
    /// and shown.</summary>
    void Attach(PlatformHandle control);

    /// <summary>Place the control. Both rectangles are in the form's client area, in
    /// logical units.</summary>
    /// <param name="bounds">All of the host: the control's size and place.</param>
    /// <param name="visible">The part of it the form shows; empty — the slot is hidden.</param>
    void Place(Rectangle bounds, Rectangle visible);

    /// <summary>Give the control the keyboard: the host was focused by the form —
    /// a Tab, a click on its edge, Focus() in code.</summary>
    void Focus();

    /// <summary>Destroy the control the slot holds: what a host does when it has no
    /// destruction of its own.</summary>
    void DestroyControl(PlatformHandle control);

    /// <summary>The control took the keyboard by itself — a click into it: the form moves
    /// its own focus to the host, so that no element of the form shows a caret while the
    /// typing goes elsewhere. Raised on the UI thread.</summary>
    event EventHandler? ControlFocused;
}