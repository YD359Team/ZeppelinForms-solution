using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Theming;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class TitleBarTests
{
    private static (Form Form, HeadlessWindow Window) Shown()
    {
        var platform = new HeadlessPlatform();
        var form = new Form { Size = new Size(300, 200) };

        var window = (HeadlessWindow)platform.CreateWindow(form);
        form.Show();

        return (form, window);
    }

    [Fact]
    public void TitleBarFollowsTheAppsThemeFromTheFirstShow()
    {
        Theme previous = App.Theme;

        try
        {
            App.Theme = Themes.Dark;
            var (_, window) = Shown();

            // it used to stay light until the theme was switched
            Assert.True(window.TitleBarIsDark);

            App.Theme = Themes.Light;
            Assert.False(window.TitleBarIsDark);
        }
        finally
        {
            App.Theme = previous;
        }
    }

    [Fact]
    public void ExplicitThemeOverridesTheApps()
    {
        Theme previous = App.Theme;

        try
        {
            App.Theme = Themes.Dark;
            var (form, window) = Shown();

            form.TitleBarTheme = TitleBarTheme.Light;

            Assert.False(window.TitleBarIsDark);
        }
        finally
        {
            App.Theme = previous;
        }
    }

    [Fact]
    public void OwnColorDecidesDarkAndIsPassedOn()
    {
        Theme previous = App.Theme;

        try
        {
            App.Theme = Themes.Light;
            var (form, window) = Shown();

            var night = new Color(255, 0x20, 0x20, 0x20);
            form.TitleBarColor = night;

            Assert.True(window.TitleBarIsDark);
            Assert.Equal(night, window.TitleBarBackground);

            form.TitleBarColor = null;

            Assert.False(window.TitleBarIsDark);
            Assert.Null(window.TitleBarBackground);
        }
        finally
        {
            App.Theme = previous;
        }
    }
}