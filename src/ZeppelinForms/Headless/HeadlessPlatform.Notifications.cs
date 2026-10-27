using ZeppelinForms.Forms;

namespace ZeppelinForms.Headless;

/// <summary>Notifications without a screen: kept, and clicked or closed by the test.</summary>
public sealed partial class HeadlessPlatform : INotificationPlatform
{
    private readonly List<Notification> _notifications = [];

    /// <summary>The notifications shown and not closed yet, the oldest first.</summary>
    public IReadOnlyList<Notification> Notifications => _notifications;

    /// <summary>Whether the next notifications are refused, as by a user who turned them off.</summary>
    public bool RefuseNotifications { get; set; }

    bool INotificationPlatform.ShowNotification(Notification notification)
    {
        if (RefuseNotifications) return false;

        notification.MarkShown();
        _notifications.Add(notification);

        return true;
    }

    /// <summary>The user clicks the notification: it is clicked, then closed, as the systems do.</summary>
    public void ClickNotification(Notification notification)
    {
        if (!_notifications.Remove(notification)) return;

        notification.RaiseClicked();
        notification.RaiseClosed();
    }

    /// <summary>The notification is dismissed or times out.</summary>
    public void CloseNotification(Notification notification)
    {
        if (_notifications.Remove(notification)) notification.RaiseClosed();
    }
}
