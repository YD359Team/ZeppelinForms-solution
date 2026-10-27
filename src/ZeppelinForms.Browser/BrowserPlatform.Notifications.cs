using ZeppelinForms.Forms;

namespace ZeppelinForms.Browser;

/// <summary>Web notifications: the Notification API, the permission asked with the first.</summary>
public sealed partial class BrowserPlatform : INotificationPlatform
{
    private readonly Dictionary<int, Notification> _notifications = [];
    private int _nextNotification;

    bool INotificationPlatform.ShowNotification(Notification notification)
    {
        if (!Interop.SupportsNotifications()) return false;

        int id = ++_nextNotification;

        notification.MarkShown();
        _notifications[id] = notification;

        Interop.ShowNotification(id, notification.Title, notification.Text ?? string.Empty, string.Empty);

        return true;
    }

    internal void HandleNotificationClicked(int id)
    {
        if (_notifications.TryGetValue(id, out Notification? notification))
            notification.RaiseClicked();
    }

    internal void HandleNotificationClosed(int id)
    {
        if (_notifications.Remove(id, out Notification? notification))
            notification.RaiseClosed();
    }
}
