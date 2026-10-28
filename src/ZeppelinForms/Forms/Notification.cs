namespace ZeppelinForms.Forms;

/// <summary>What a notification is about: the icon the system shows beside it.</summary>
public enum NotificationKind
{
    /// <summary>No icon of its own: the application's.</summary>
    None,

    Information,

    Warning,

    Error,
}

/// <summary>
/// A message the system shows outside the application's windows: in the corner of the
/// screen and in its notification center — a download finished, a message came.
/// <see cref="Notifications.Show(Notification)"/> shows it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Clicked"/> comes on the UI thread when the user clicks the notification —
/// the moment to bring the form forward; <see cref="Closed"/> when it goes, clicked,
/// dismissed or timed out. Where a system keeps notifications in its center the click may
/// come much later, while the application still runs.
/// </para>
/// </remarks>
public sealed class Notification
{
    public Notification(string title, string? text = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Title = title;
        Text = text;
    }

    /// <summary>The first line, in bold as a rule.</summary>
    public string Title { get; }

    /// <summary>What it says under the title; null — the title alone.</summary>
    public string? Text { get; init; }

    /// <summary>The icon beside it.</summary>
    public NotificationKind Kind { get; init; } = NotificationKind.Information;

    /// <summary>The user clicked the notification.</summary>
    public event EventHandler? Clicked;

    /// <summary>The notification went: clicked, dismissed, timed out, or replaced by
    /// another of the same tray icon on Windows. Once.</summary>
    public event EventHandler? Closed;

    /// <summary>The system shows it now, or keeps it in its center.</summary>
    public bool IsShown { get; private set; }

    internal void MarkShown() => IsShown = true;

    internal void RaiseClicked()
    {
        if (!IsShown) return;

        Clicked?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseClosed()
    {
        if (!IsShown) return;

        IsShown = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// The system's notifications: a toast on Windows, a notification of the desktop on Linux,
/// a web notification in the browser.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Windows</b>: a balloon of a tray icon, which Windows 10 and 11 show as a
/// toast. A visible <see cref="TrayIcon"/> of the application carries it; without one an
/// icon of the application is put into the tray for as long as the notification lasts.</item>
/// <item><b>Linux</b>, X11 and Wayland alike: <c>org.freedesktop.Notifications</c> on the
/// session bus, which every desktop has. The application's .desktop file, found by
/// <see cref="App.Id"/>, gives it its name and icon there.</item>
/// <item><b>The browser</b>: the Notification API. The first notification asks the user's
/// permission; refused, nothing shows and <see cref="Notification.Closed"/> comes.</item>
/// </list>
/// <para><see cref="PlatformCapabilities.Notifications"/> tells whether there is a way.</para>
/// </remarks>
public static class Notifications
{
    /// <summary>Show a notification. False — the platform has no notifications, or the
    /// system refused this one at once; a refusal that comes later — a browser's
    /// permission — closes it instead.</summary>
    public static bool Show(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return App.CurrentPlatform is INotificationPlatform platform && platform.ShowNotification(notification);
    }

    /// <summary>Show a notification of a title and a text; null — the platform has none.</summary>
    public static Notification? Show(string title, string? text = null, NotificationKind kind = NotificationKind.Information)
    {
        var notification = new Notification(title, text) { Kind = kind };

        return Show(notification) ? notification : null;
    }
}

/// <summary>A platform that shows the system's notifications. True — shown: the platform
/// calls <see cref="Notification.MarkShown"/> before, and raises its Clicked and Closed.</summary>
internal interface INotificationPlatform
{
    bool ShowNotification(Notification notification);
}
