using Xunit;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>The application's icon in the system tray: shown and taken off by
/// <see cref="TrayIcon.Visible"/>, following its icon and tooltip, its click and its
/// menu played through the headless tray.</summary>
[Collection("Platform")]
public sealed class TrayIconTests
{
    private static HeadlessPlatform Platform()
    {
        var platform = new HeadlessPlatform();
        _ = new App(platform) { MainForm = new Form() };
        return platform;
    }

    [Fact]
    public void AnIconIsInTheTrayWhileVisible()
    {
        HeadlessPlatform platform = Platform();
        using var tray = new TrayIcon { ToolTip = "Orders" };

        Assert.False(tray.IsShown);
        Assert.Empty(platform.TrayIcons);

        tray.Visible = true;

        HeadlessTrayIcon shown = Assert.Single(platform.TrayIcons);
        Assert.True(tray.IsShown);
        Assert.Equal("Orders", shown.ToolTip);
        Assert.Same(Assets.Logo, shown.Icon);

        tray.ToolTip = "Orders: 3 new";
        Assert.Equal("Orders: 3 new", shown.ToolTip);

        tray.Visible = false;

        Assert.True(shown.IsDisposed);
        Assert.Empty(platform.TrayIcons);
        Assert.False(tray.IsShown);
    }

    [Fact]
    public void AClickAndADoubleClickAreRaised()
    {
        HeadlessPlatform platform = Platform();
        using var tray = new TrayIcon { Visible = true };
        int clicks = 0, doubles = 0;
        tray.Click += (_, _) => clicks++;
        tray.DoubleClick += (_, _) => doubles++;

        platform.TrayIcons[0].Click();
        platform.TrayIcons[0].DoubleClick();

        Assert.Equal(1, clicks);
        Assert.Equal(1, doubles);
    }

    [Fact]
    public void TheMenuRunsItsItemsAsAMenuBarDoes()
    {
        HeadlessPlatform platform = Platform();
        using var tray = new TrayIcon { Visible = true };

        var log = new List<string>();
        var open = new MenuItem { Text = "Open" };
        open.Click += (_, _) => log.Add("open");

        var mute = new MenuItem { Text = "Mute", IsCheckable = true };
        var disabled = new MenuItem { Text = "Later", IsEnabled = false };

        var more = new MenuItem { Text = "More" };
        var about = new MenuItem { Text = "About" };
        about.Click += (_, _) => log.Add("about");
        more.Items.Add(about);

        tray.Menu.AddRange([open, mute, MenuItem.Separator, disabled, more]);
        tray.MenuOpening += (_, _) => log.Add("opening");

        HeadlessTrayIcon shown = platform.TrayIcons[0];

        Assert.True(shown.Choose(0));
        Assert.True(shown.Choose(1));
        Assert.True(mute.IsChecked);
        Assert.False(shown.Choose(2));
        Assert.False(shown.Choose(3));
        Assert.True(shown.Choose(4, 0));

        Assert.Equal(["opening", "open", "opening", "opening", "opening", "opening", "about"], log);
    }

    [Fact]
    public void TheApplicationsStartUpdatesTheIconsAndItsEndTakesThemOff()
    {
        var platform = new HeadlessPlatform();
        _ = new App(platform) { MainForm = new Form() };

        // as the main form's constructor would, inside the App's initializer
        using var tray = new TrayIcon { Visible = true };
        Assert.True(tray.IsShown);

        tray.Icon = null;
        TrayIcon.OnAppStarted();
        Assert.True(platform.TrayIcons[0].UpdateCount >= 1);

        TrayIcon.OnAppEnded();
        Assert.False(tray.IsShown);
        Assert.Empty(platform.TrayIcons);
    }

    [Fact]
    public void ADisposedIconCantBeShown()
    {
        Platform();
        var tray = new TrayIcon { Visible = true };

        tray.Dispose();

        Assert.False(tray.IsShown);
        Assert.Throws<ObjectDisposedException>(() => tray.Visible = true);
    }
}