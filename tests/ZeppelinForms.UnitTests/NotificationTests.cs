using Xunit;
using ZeppelinForms.Forms;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.19: the system's notifications — shown, clicked and closed through the
/// headless platform, and from a tray icon.</summary>
[Collection("Platform")]
public sealed class NotificationTests
{
    private static HeadlessPlatform Platform()
    {
        var platform = new HeadlessPlatform();
        _ = new App(platform) { MainForm = new Form() };
        return platform;
    }

    [Fact]
    public void ANotificationIsShownClickedAndClosed()
    {
        HeadlessPlatform platform = Platform();
        var events = new List<string>();

        var notification = new Notification("Saved", "orders.csv") { Kind = NotificationKind.Warning };
        notification.Clicked += (_, _) => events.Add("clicked");
        notification.Closed += (_, _) => events.Add("closed");

        Assert.True(Notifications.Show(notification));
        Assert.True(notification.IsShown);
        Assert.Same(notification, Assert.Single(platform.Notifications));

        platform.ClickNotification(notification);

        Assert.Equal(["clicked", "closed"], events);
        Assert.False(notification.IsShown);
        Assert.Empty(platform.Notifications);
    }

    [Fact]
    public void ClosedComesOnce()
    {
        HeadlessPlatform platform = Platform();
        Notification shown = Notifications.Show("Done")!;
        int closed = 0;
        shown.Closed += (_, _) => closed++;

        platform.CloseNotification(shown);
        platform.CloseNotification(shown);
        shown.RaiseClosed();

        Assert.Equal(1, closed);
    }

    [Fact]
    public void ARefusedNotificationIsNotShown()
    {
        HeadlessPlatform platform = Platform();
        platform.RefuseNotifications = true;

        Assert.Null(Notifications.Show("Done", "nobody sees this"));
        Assert.Empty(platform.Notifications);
    }

    [Fact]
    public void ATrayIconWithoutAWayOfItsOwnSendsItAsAnyOther()
    {
        HeadlessPlatform platform = Platform();
        using var tray = new TrayIcon { Visible = true };

        Notification? notification = tray.ShowNotification("Synced", "3 files");

        Assert.NotNull(notification);
        Assert.Same(notification, Assert.Single(platform.Notifications));
        Assert.Equal("3 files", notification!.Text);
        Assert.Equal(NotificationKind.Information, notification.Kind);
    }

    [Fact]
    public void ATitleIsRequired() =>
        Assert.Throws<ArgumentException>(() => new Notification(" "));
}
