using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Windows;

/// <summary>The frame of the window from the form's properties, at creation and
/// whenever they change while the window is open.</summary>
/// <remarks>
/// <para>
/// The styles are those WinForms sets for the same properties: a caption with or
/// without the system menu, a sizing frame or a thin one, the dialog frame
/// without the icon, the narrow caption of a tool window. None is a popup: no
/// caption and no frame at all.
/// </para>
/// <para>
/// A top-level window is in the taskbar unless it is owned or a tool window. A
/// window that must stay out of it without the tool window's narrow caption is
/// owned by a hidden window of its own, as WinForms does it.
/// </para>
/// </remarks>
internal sealed partial class Win32Window
{
    // the bits of the frame the form decides; the rest — visible, minimized,
    // disabled, layered for the opacity — belong to the window's state
    private const uint ChromeStyleMask =
        NativeConstants.WS_POPUP | NativeConstants.WS_CAPTION | NativeConstants.WS_SYSMENU |
        NativeConstants.WS_THICKFRAME | NativeConstants.WS_MINIMIZEBOX | NativeConstants.WS_MAXIMIZEBOX;

    private const uint ChromeExStyleMask =
        NativeConstants.WS_EX_DLGMODALFRAME | NativeConstants.WS_EX_TOOLWINDOW |
        NativeConstants.WS_EX_WINDOWEDGE | NativeConstants.WS_EX_APPWINDOW;

    private static nint s_taskbarlessOwner;

    /// <summary>The styles of the frame the form asks for.</summary>
    private (uint Style, uint ExStyle) ChromeStyles()
    {
        uint style;
        uint exStyle = 0;

        if (_form.FormBorderStyle == FormBorderStyle.None)
        {
            // no caption to draw the buttons on; the system menu still gives
            // Alt+F4 and the taskbar button's menu, the minimize box — minimizing
            // from the taskbar
            style = NativeConstants.WS_POPUP;

            if (_form.ControlBox) style |= NativeConstants.WS_SYSMENU;
            if (_form.HasMinimizeBox) style |= NativeConstants.WS_MINIMIZEBOX;
        }
        else
        {
            style = NativeConstants.WS_CAPTION;

            // the buttons are part of the system menu: without it there are none
            if (_form.ControlBox) style |= NativeConstants.WS_SYSMENU;
            if (_form.HasMinimizeBox) style |= NativeConstants.WS_MINIMIZEBOX;
            if (_form.HasMaximizeBox) style |= NativeConstants.WS_MAXIMIZEBOX;
            if (_form.IsResizable) style |= NativeConstants.WS_THICKFRAME;

            exStyle |= NativeConstants.WS_EX_WINDOWEDGE;

            if (_form.FormBorderStyle == FormBorderStyle.FixedDialog)
                exStyle |= NativeConstants.WS_EX_DLGMODALFRAME;
        }

        if (_form.IsToolWindow)
            exStyle |= NativeConstants.WS_EX_TOOLWINDOW;
        else if (_form.ShowInTaskbar)
            exStyle |= NativeConstants.WS_EX_APPWINDOW;

        return (style, exStyle);
    }

    /// <summary>The owner that keeps the window out of the taskbar, or none.</summary>
    private nint ChromeOwner() =>
        _form.ShowInTaskbar || _form.IsToolWindow ? 0 : TaskbarlessOwner();

    /// <summary>A hidden window that is never shown and lives as long as the process:
    /// the windows it owns have no taskbar button.</summary>
    private static nint TaskbarlessOwner()
    {
        if (s_taskbarlessOwner == 0)
        {
            s_taskbarlessOwner = NativeMethods.CreateWindowEx(
                0, "STATIC", string.Empty, NativeConstants.WS_POPUP,
                0, 0, 0, 0, 0, 0,
                NativeMethods.GetModuleHandle(null), 0);
        }

        return s_taskbarlessOwner;
    }

    /// <summary>Rebuild the frame of an open window.</summary>
    public void UpdateChrome()
    {
        if (_handle == 0) return;

        (uint style, uint exStyle) = ChromeStyles();

        nint currentStyle = NativeMethods.GetWindowLongPtr(_handle, NativeConstants.GWL_STYLE);
        nint currentExStyle = NativeMethods.GetWindowLongPtr(_handle, NativeConstants.GWL_EXSTYLE);

        // unchecked: WS_POPUP is the sign bit of a 32-bit style
        nint newStyle = (currentStyle & ~unchecked((nint)ChromeStyleMask)) | unchecked((nint)style);
        nint newExStyle = (currentExStyle & ~(nint)ChromeExStyleMask) | (nint)exStyle;

        nint owner = ChromeOwner();
        bool ownerChanged = NativeMethods.GetWindowLongPtr(_handle, NativeConstants.GWLP_HWNDPARENT) != owner;
        bool taskbarChanged = ownerChanged ||
            ((currentExStyle ^ newExStyle) & (nint)(NativeConstants.WS_EX_TOOLWINDOW | NativeConstants.WS_EX_APPWINDOW)) != 0;

        // the taskbar reads the styles when a window is shown: a visible window
        // is hidden for the change and shown again, without taking the activation
        bool reshow = taskbarChanged && NativeMethods.IsWindowVisible(_handle);

        if (reshow)
            NativeMethods.ShowWindow(_handle, NativeConstants.SW_HIDE);

        NativeMethods.SetWindowLongPtr(_handle, NativeConstants.GWL_STYLE, newStyle);
        NativeMethods.SetWindowLongPtr(_handle, NativeConstants.GWL_EXSTYLE, newExStyle);

        if (ownerChanged)
            NativeMethods.SetWindowLongPtr(_handle, NativeConstants.GWLP_HWNDPARENT, owner);

        // the frame is cached by the system until it is told it changed
        NativeMethods.SetWindowPos(
            _handle, 0, 0, 0, 0, 0,
            NativeConstants.SWP_NOMOVE | NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOZORDER |
            NativeConstants.SWP_NOACTIVATE | NativeConstants.SWP_FRAMECHANGED);

        if (reshow)
            NativeMethods.ShowWindow(_handle, NativeConstants.SW_SHOWNA);
    }
}