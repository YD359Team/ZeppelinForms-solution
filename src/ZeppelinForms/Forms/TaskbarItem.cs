namespace ZeppelinForms.Forms;

/// <summary>What the progress on a window's taskbar button shows.</summary>
public enum TaskbarProgressState
{
    /// <summary>No progress: the button as it is.</summary>
    None,

    /// <summary>Work of unknown length: a bar that moves without filling.</summary>
    Indeterminate,

    /// <summary>The bar filled to <see cref="TaskbarItem.ProgressValue"/>.</summary>
    Normal,

    /// <summary>Filled, and paused — yellow on Windows.</summary>
    Paused,

    /// <summary>Filled, and failed — red on Windows.</summary>
    Error,
}

/// <summary>
/// A form's button on the taskbar or the dock: the progress of a long work and a count —
/// a download, unread messages — seen without opening the window.
/// </summary>
/// <remarks>
/// <para>
/// Each platform shows what it can, and what it can't is simply not shown —
/// <see cref="PlatformCapabilities.TaskbarProgress"/> and
/// <see cref="PlatformCapabilities.TaskbarBadge"/> tell which:
/// </para>
/// <list type="bullet">
/// <item><b>Windows</b>: the progress in the taskbar button, in its five states; the
/// count as an overlay badge in its corner.</item>
/// <item><b>Linux</b>, X11 and Wayland alike: both through the launcher entry of the
/// application, <c>com.canonical.Unity.LauncherEntry</c>, which KDE's task manager, Dash
/// to Dock, Plank and others show. It is the application's, not the window's: they find
/// the application's .desktop file by <see cref="App.Id"/>, and several forms share it.</item>
/// <item><b>The browser</b>: the count as the badge of an installed web application;
/// there is no progress.</item>
/// </list>
/// <para>
/// The values are kept: set before the form is shown, they appear with its window.
/// </para>
/// </remarks>
public sealed class TaskbarItem
{
    private readonly Form _form;

    internal TaskbarItem(Form form)
    {
        _form = form;
    }

    /// <summary>What the progress shows; <see cref="TaskbarProgressState.None"/> — none.</summary>
    public TaskbarProgressState ProgressState
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            ApplyProgress();
        }
    }

    /// <summary>How far the work went, from 0 to 1. A value set while the state is
    /// <see cref="TaskbarProgressState.None"/> turns the state to
    /// <see cref="TaskbarProgressState.Normal"/>: setting the value is asking for the bar.</summary>
    public double ProgressValue
    {
        get;
        set
        {
            value = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);

            if (field == value && ProgressState != TaskbarProgressState.None) return;

            field = value;

            if (ProgressState == TaskbarProgressState.None)
                ProgressState = TaskbarProgressState.Normal;
            else
                ApplyProgress();
        }
    }

    /// <summary>A count on the button: unread messages, waiting tasks. Null or 0 — none.</summary>
    public int? Badge
    {
        get;
        set
        {
            value = value is > 0 ? value : null;

            if (field == value) return;

            field = value;
            ApplyBadge();
        }
    }

    /// <summary>Back to a plain button: no progress, no badge.</summary>
    public void Clear()
    {
        ProgressState = TaskbarProgressState.None;
        Badge = null;
    }

    private void ApplyProgress() =>
        (_form.PlatformWindow as ITaskbarWindow)?.SetTaskbarProgress(ProgressState, ProgressValue);

    private void ApplyBadge() =>
        (_form.PlatformWindow as ITaskbarWindow)?.SetTaskbarBadge(Badge);

    /// <summary>A window was made for the form: it shows what was set before.</summary>
    internal void ApplyAll()
    {
        if (ProgressState != TaskbarProgressState.None) ApplyProgress();
        if (Badge is not null) ApplyBadge();
    }
}
