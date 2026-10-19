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