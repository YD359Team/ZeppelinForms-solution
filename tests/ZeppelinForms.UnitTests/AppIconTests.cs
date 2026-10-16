using Xunit;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.16: <see cref="App.Icon"/> is the icon of every form without one of its
/// own — it went nowhere, and the main form took the logo regardless.</summary>
[Collection("Platform")]
public class AppIconTests
{
    /// <summary>An icon of its own, not the logo: read from the logo's bytes.</summary>
    private static Icon OtherIcon()
    {
        using var stream = new MemoryStream(Assets.Logo.GetRawData().ToArray());
        return Icon.FromStream(stream);
    }

    /// <summary>Run an application whose main form closes as soon as it is shown.</summary>
    private static void RunAndClose(App app)
    {
        SynchronizationContext? context = SynchronizationContext.Current;

        try
        {
            app.MainForm.Shown += (_, _) => app.MainForm.Close();
            app.Run();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    [Fact]
    public void TheApplicationsIconGoesToItsForms()
    {
        Icon icon = OtherIcon();
        var main = new Form { Size = new Size(200, 100) };

        RunAndClose(new App(new HeadlessPlatform()) { MainForm = main, Icon = icon });

        Assert.Same(icon, main.Icon);

        // a form made later, without an icon of its own, shows the application's
        var dialog = new Form();
        Assert.Null(dialog.Icon);
        Assert.Same(icon, dialog.EffectiveIcon);

        // and one with its own keeps it
        Icon own = OtherIcon();
        dialog.Icon = own;
        Assert.Same(own, dialog.EffectiveIcon);
    }

    [Fact]
    public void WithoutAnApplicationIconTheLogoIsUsed()
    {
        var main = new Form { Size = new Size(200, 100) };

        RunAndClose(new App(new HeadlessPlatform()) { MainForm = main });

        Assert.Same(Assets.Logo, main.Icon);
        Assert.Same(Assets.Logo, new Form().EffectiveIcon);
    }
}
