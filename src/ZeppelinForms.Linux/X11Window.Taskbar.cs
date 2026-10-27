using System.Text;
using ZeppelinForms.Forms;
using ZeppelinForms.Linux.Common;

namespace ZeppelinForms.Linux;

/// <summary>The window as the window manager and the docks know the application: its
/// class (WM_CLASS), the launcher entry's progress and count, and demanding attention.</summary>
internal sealed partial class X11Window : ITaskbarWindow
{
    /// <summary>WM_CLASS and STRING, predefined atoms of the core protocol.</summary>
    private const nuint XA_WM_CLASS = 67;
    private const nuint XA_STRING = 31;

    private const int NetWmStateAdd = 1;

    /// <summary>WM_CLASS: the instance and the class, both <see cref="App.Id"/>. The manager
    /// groups the windows by it, and the docks find the application's .desktop file by its
    /// StartupWMClass — without it a window had none, and a dock showed it as an unknown
    /// application, apart from the launcher it was started from.</summary>
    private void ApplyWindowClass()
    {
        // two strings, each ended by a zero: "instance\0class\0"
        byte[] value = Encoding.ASCII.GetBytes($"{App.Id}\0{App.Id}\0");

        X11.XChangeProperty(_display, _window, XA_WM_CLASS, XA_STRING, 8, X11.PropModeReplace, value, value.Length);
    }

    public void SetTaskbarProgress(TaskbarProgressState state, double value) =>
        LauncherEntry.SetProgress(state, value);

    public void SetTaskbarBadge(int? count) => LauncherEntry.SetCount(count);

    /// <summary>_NET_WM_STATE_DEMANDS_ATTENTION: the task bar marks the window — KDE and
    /// XFCE make it blink — until it is activated, when the manager takes the state away.</summary>
    public void RequestAttention()
    {
        if (_window == 0) return;

        SendNetWmState(NetWmStateAdd, X11.XInternAtom(_display, "_NET_WM_STATE_DEMANDS_ATTENTION", false));
        X11.XFlush(_display);
    }
}
