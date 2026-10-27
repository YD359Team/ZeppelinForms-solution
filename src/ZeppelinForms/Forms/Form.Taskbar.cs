namespace ZeppelinForms.Forms;

/// <summary>The form's button on the taskbar or the dock, asking for attention, and
/// coming forward.</summary>
public partial class Form
{
    /// <summary>The form's button on the taskbar or the dock: progress and a badge.
    /// See <see cref="TaskbarItem"/> for what each platform shows.</summary>
    public TaskbarItem Taskbar => _taskbar ??= new TaskbarItem(this);

    private TaskbarItem? _taskbar;

    /// <summary>Ask for the user's attention without taking the focus from where it is:
    /// a long work finished, a message came. The taskbar button flashes on Windows; on
    /// Linux the window is marked as demanding attention; in the browser the tab's title
    /// blinks while the page is in the background. It stops when the window is activated.</summary>
    /// <remarks>An active window has the attention already, and nothing happens.
    /// <see cref="PlatformCapabilities.WindowAttention"/> tells where it works.</remarks>
    public void RequestAttention()
    {
        if (IsActive) return;

        (PlatformWindow as ITaskbarWindow)?.RequestAttention();
    }

    /// <summary>Bring the window forward and give it the keyboard, as WinForms'
    /// <c>Form.Activate</c> does: the answer to a click on a notification or on a tray icon.
    /// A hidden form is shown again first; a form never shown is left as it is.</summary>
    /// <remarks>The systems don't let an application take the focus whenever it likes:
    /// Windows from the background and Wayland without the user's input may only flash
    /// the button or mark the window instead. A click on a notification or a tray icon is
    /// such input, and there it works.</remarks>
    public void Activate()
    {
        if (PlatformWindow is null) return;

        if (!Visible) Show();

        PlatformWindow?.Activate();
    }
}
