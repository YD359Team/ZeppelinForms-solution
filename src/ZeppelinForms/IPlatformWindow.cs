using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms;

/// <summary>The minimum no platform can do without.</summary>
public interface IPlatformWindow
{
    /// <summary>Show the window. In the browser and on Android this is not creating
    /// a window but connecting the form to an existing host surface.</summary>
    void Show();

    /// <summary>Close the window. After the surface is destroyed the platform must
    /// call Form.OnWindowClosed: both the async dialog and the count of live
    /// windows rely on it.</summary>
    void Close();

    void Invalidate(Rectangle? rect);

    /// <summary>The ratio of physical pixels to logical ones.</summary>
    float Scale { get; }

    /// <summary>Run an action on the UI thread.</summary>
    void Invoke(Action action);

    void CaptureMouse();
    void ReleaseMouseCapture();
    void SetCursor(CursorKind cursor);
    /// <summary>A cursor drawn from a picture. A platform without cursors of its own
    /// shows the picture's fallback — the default.</summary>
    void SetCursor(CursorImage cursor) => SetCursor(cursor.Fallback);

    /// <summary>Register the window as a system drag-and-drop target.
    /// Without this AllowDrop on elements won't work.</summary>
    void SetDragDropEnabled(bool enabled);

    /// <summary>Disable or enable input into this window. This is how modality
    /// is done: the dialog's owner goes deaf while the dialog is open.</summary>
    void SetEnabled(bool enabled);

    /// <summary>Make the window active.</summary>
    void Activate();

    /// <summary>The title bar dark or light, and its own colors where the platform has
    /// them. Called by the form when it is shown and whenever its title bar or the theme
    /// changes. A platform without a title bar does nothing — the default.</summary>
    /// <param name="background">null — the system's color.</param>
    /// <param name="foreground">The caption's text on <paramref name="background"/>; null with it.</param>
    void SetTitleBarAppearance(bool isDark, Color? background, Color? foreground) { }

    IFrameDriver Frames { get; }
}

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
}