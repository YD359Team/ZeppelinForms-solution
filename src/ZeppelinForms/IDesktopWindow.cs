using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms;

/// <summary>The window's appearance as a desktop object. Not implemented in the
/// browser and on Android — those concepts don't exist there.</summary>
/// <remarks>
/// Resizability has no method of its own: it is <c>Form.CanResize</c> and the frame,
/// rebuilt by <see cref="UpdateChrome"/>. The members added after 0.16 have bodies
/// that do nothing, so a window of an external platform package keeps compiling.
/// </remarks>
public interface IDesktopWindow
{
    void SetTitle(string? title);

    /// <summary>Move and resize the window: <c>Form.Position</c> or <c>Form.Size</c>
    /// was set while it is open. The units are those of the two properties — the
    /// position in screen pixels, the size in logical units and measured as Form.Size
    /// is. Within the form's size limits. The platform reports the window's moves and
    /// resizes back through the form, whoever made them.</summary>
    void SetBounds(Rectangle bounds);
    void SetOpacity(float opacity);
    void SetWindowState(WindowState state);
    /// <summary>The window's icon changed while it is open. Null — the application's.
    /// A platform without window icons does nothing — the default.</summary>
    void SetIcon(Drawing.Imaging.Icon? icon) { }

    bool SupportsTransparency { get; }

    /// <summary>Rebuild the frame from the form: FormBorderStyle, ControlBox,
    /// ShowInTaskbar, CanMinimize, CanMaximize, CanResize changed while the window
    /// is open. A platform that reads them only when creating the window keeps
    /// this empty.</summary>
    void UpdateChrome() { }

    /// <summary>The limits the user's dragging of the edges keeps within, in logical
    /// units and measured as <c>Form.Size</c> is; an axis of NaN — no limit. Called
    /// whenever <c>Form.MinimumSize</c> or <c>Form.MaximumSize</c> of an open window
    /// changes, and a window that is out of them is brought in; when making the
    /// window the platform reads the form's limits itself, as it does the frame.</summary>
    void SetSizeLimits(Size minimum, Size maximum) { }

    /// <summary>Keep the window above every window that isn't topmost, or stop.</summary>
    void SetTopMost(bool topMost) { }

    /// <summary>Move the window to the middle of the working area of the screen it is on.</summary>
    void CenterOnScreen() { }

    /// <summary>Take the shown window off the screen and the taskbar without destroying
    /// it, or bring it back as it was and make it active: <c>Form.Hide</c> and the
    /// <c>Form.Show</c> after it. A hidden window draws nothing. A platform of its own
    /// that can't hide keeps the default, which refuses.</summary>
    void SetVisible(bool visible) =>
        throw new PlatformNotSupportedException($"{GetType().Name} can't hide a window.");


    /// <summary>The window can be placed by the application: false where the compositor
    /// alone decides — Wayland. The form then refuses <c>Form.Position</c> before
    /// asking the window anything.</summary>
    bool SupportsPositioning => true;
}