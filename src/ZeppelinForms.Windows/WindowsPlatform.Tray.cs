using ZeppelinForms.Forms;

namespace ZeppelinForms.Windows;

/// <summary>The notification area of the taskbar: every <see cref="TrayIcon"/> is a
/// Shell_NotifyIcon icon with a hidden window of its own.</summary>
public partial class WindowsPlatform : ITrayPlatform, INotificationPlatform
{
    ITrayIconImpl? ITrayPlatform.CreateTrayIcon(TrayIcon owner) => new Win32TrayIcon(owner);

    /// <summary>The application's icon put into the tray for a notification when the
    /// application shows none of its own.</summary>
    private TrayIcon? _notificationIcon;

    /// <summary>The notifications the borrowed icon carries now: it leaves the tray with
    /// the last of them.</summary>
    private int _notificationsOnIcon;

    /// <summary>A notification is a balloon of a tray icon: of one the application shows,
    /// or of the application's icon put into the tray for as long as the notification
    /// lasts — Windows has no balloon without an icon, and a toast of its own needs a
    /// registered application user model id and a shortcut in the Start menu.</summary>
    bool INotificationPlatform.ShowNotification(Notification notification)
    {
        if (TrayIcon.FirstShown is { } shown && !ReferenceEquals(shown, _notificationIcon))
            return shown.ShowThroughTray(notification);

        _notificationIcon ??= new TrayIcon { ToolTip = App.DisplayName };
        _notificationIcon.Visible = true;

        if (!_notificationIcon.ShowThroughTray(notification))
        {
            if (_notificationsOnIcon == 0) _notificationIcon.Visible = false;
            return false;
        }

        _notificationsOnIcon++;

        notification.Closed += (_, _) =>
        {
            if (--_notificationsOnIcon == 0 && _notificationIcon is { } icon)
                icon.Visible = false;
        };

        return true;
    }
}