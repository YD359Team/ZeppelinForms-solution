using ZeppelinForms.Forms;

namespace ZeppelinForms;

/// <summary>A window with a button on the taskbar or the dock: its progress, its badge and
/// a request for the user's attention. Implemented by the platform windows that have one;
/// a form asks for it and does nothing where it isn't.</summary>
/// <remarks>Each member is told the whole state, not a change: a platform that can't keep a
/// part of it — a dock that shows the badge alone — drops that part.</remarks>
public interface ITaskbarWindow
{
    /// <summary>The progress of the button: <paramref name="value"/> from 0 to 1, read
    /// for <see cref="TaskbarProgressState.Normal"/>, <see cref="TaskbarProgressState.Paused"/>
    /// and <see cref="TaskbarProgressState.Error"/>.</summary>
    void SetTaskbarProgress(TaskbarProgressState state, double value);

    /// <summary>The count on the button; null — none.</summary>
    void SetTaskbarBadge(int? count);

    /// <summary>Ask for the user's attention without taking the focus: the button flashes
    /// or is marked until the window is activated.</summary>
    void RequestAttention();
}
