using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Headless;
using ZeppelinForms.Theming;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// The theme set where the application is described, App.StartupTheme. The static
/// App.Theme can't be set from an object initializer, and before this the Fluent
/// themes could be reached only by a separate line nothing pointed to.
/// </summary>
[Collection("Platform")]
public class StartupThemeTests
{
    private static void Restoring(Action test)
    {
        Theme themeBefore = App.Theme;
        Font fontBefore = Font.Default;

        try
        {
            test();
        }
        finally
        {
            App.UseSystemAppearance(null);
            App.Theme = themeBefore;
            Font.Default = fontBefore;
        }
    }

    [Fact]
    public void StartupThemeIsSetFromInitializer()
    {
        Restoring(() =>
        {
            var button = new Button { Text = "x" };

            _ = new App(new HeadlessPlatform())
            {
                StartupTheme = Themes.FluentLight,
                MainForm = new Form { Size = new Size(200, 80), Content = button },
            };

            Assert.Same(Themes.FluentLight, App.Theme);
        });
    }

    [Fact]
    public void FormBuiltAfterItIsStyledByIt()
    {
        Restoring(() =>
        {
            var button = new Button { Text = "x" };
            var form = new Form { Size = new Size(200, 80) };

            var app = new App(new HeadlessPlatform())
            {
                StartupTheme = Themes.FluentDark,
                MainForm = form,
            };

            form.Content = button;
            new HeadlessPlatform().CreateWindow(form);
            form.UpdateLayout();

            Assert.False(button.RippleEnabled);
            Assert.Equal(Themes.FluentDark.Colors.ControlStrokeSecondary, button.ElevationBorderColor);
            Assert.Same(Themes.FluentDark, app.StartupTheme);
        });
    }

    [Fact]
    public void NoStartupThemeKeepsFollowingTheSystem()
    {
        Restoring(() =>
        {
            App.UseSystemTheme(Themes.Light, Themes.Dark, followAccent: false);

            _ = new App(new HeadlessPlatform()) { MainForm = new Form() };

            Assert.True(App.IsFollowingSystemTheme);
        });
    }

    [Fact]
    public void StartupThemeIsAnExplicitChoice()
    {
        Restoring(() =>
        {
            App.UseSystemTheme(Themes.Light, Themes.Dark, followAccent: false);

            _ = new App(new HeadlessPlatform())
            {
                StartupTheme = Themes.FluentLight,
                MainForm = new Form(),
            };

            Assert.False(App.IsFollowingSystemTheme);
            Assert.Same(Themes.FluentLight, App.Theme);
        });
    }
}