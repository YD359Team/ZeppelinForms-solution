using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

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

    /// <summary>Register the window as a system drag-and-drop target.
    /// Without this AllowDrop on elements won't work.</summary>
    void SetDragDropEnabled(bool enabled);

    /// <summary>Disable or enable input into this window. This is how modality
    /// is done: the dialog's owner goes deaf while the dialog is open.</summary>
    void SetEnabled(bool enabled);

    /// <summary>Make the window active.</summary>
    void Activate();

    IFrameDriver Frames { get; }
}

/// <summary>The window's appearance as a desktop object. Not implemented in the
/// browser and on Android — those concepts don't exist there.</summary>
// TODO: SetIcon, SetResizable, SetTopMost, CenterOnScreen —
// when implementations appear in Win32Window and X11Window
public interface IDesktopWindow
{
    void SetTitle(string? title);
    void SetBounds(Rectangle bounds);
    void SetOpacity(float opacity);
    void SetWindowState(WindowState state);

    bool SupportsTransparency { get; }


    /// <summary>Rebuild the frame from the form: FormBorderStyle, ControlBox,
    /// ShowInTaskbar, CanMinimize, CanMaximize, CanResize changed while the window
    /// is open. A platform that reads them only when creating the window keeps
    /// this empty.</summary>
    void UpdateChrome() { }
}