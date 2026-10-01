using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Theming;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// Following the system's appearance: App.UseSystemTheme over an ISystemAppearance,
/// and the accent taken in by each theme's rule. The system is a fake the test
/// drives; every test disconnects it and sets App.Theme back, which also stops
/// the following — App is static, and the next test must not inherit either.
/// </summary>
[Collection("Platform")]
public class SystemThemeTests
{
    private sealed class FakeAppearance : ISystemAppearance
    {
        public bool IsDark { get; set; }

        public AccentPalette? Palette { get; set; }

        public Color? AccentColor => Palette?.Accent;

        public AccentPalette? AccentPalette => Palette;

        public event EventHandler? Changed;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void Following(Action test)
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

    // ===== the mode =====

    [Fact]
    public void FollowsSystemMode()
    {
        Following(() =>
        {
            var system = new FakeAppearance();
            App.UseSystemAppearance(system);

            App.UseSystemTheme(Themes.Light, Themes.Dark, followAccent: false);
            Assert.Same(Themes.Light, App.Theme);

            system.IsDark = true;
            system.RaiseChanged();

            Assert.Same(Themes.Dark, App.Theme);
        });
    }

    [Fact]
    public void ThemeSetInCodeStopsFollowing()
    {
        Following(() =>
        {
            var system = new FakeAppearance();
            App.UseSystemAppearance(system);
            App.UseSystemTheme(Themes.Light, Themes.Dark, followAccent: false);

            App.Theme = Themes.FluentLight;
            Assert.False(App.IsFollowingSystemTheme);

            system.IsDark = true;
            system.RaiseChanged();

            Assert.Same(Themes.FluentLight, App.Theme);
        });
    }

    [Fact]
    public void SystemConnectedLaterIsPickedUp()
    {
        Following(() =>
        {
            // the application asked before the platform was created
            App.UseSystemAppearance(null);
            App.UseSystemTheme(Themes.Light, Themes.Dark, followAccent: false);

            Assert.Same(Themes.Light, App.Theme);

            App.UseSystemAppearance(new FakeAppearance { IsDark = true });

            Assert.Same(Themes.Dark, App.Theme);
        });
    }

    [Fact]
    public void NothingNewKeepsTheSameTheme()
    {
        Following(() =>
        {
            var system = new FakeAppearance { Palette = new AccentPalette(Colors.Green) };
            App.UseSystemAppearance(system);
            App.UseSystemTheme(Themes.Light, Themes.Dark);

            Theme first = App.Theme;
            int switches = 0;
            EventHandler count = (_, _) => switches++;
            App.ThemeChanged += count;

            try
            {
                // WM_SETTINGCHANGE comes for any setting; nothing here changed
                system.RaiseChanged();
                system.RaiseChanged();
            }
            finally
            {
                App.ThemeChanged -= count;
            }

            Assert.Same(first, App.Theme);
            Assert.Equal(0, switches);
        });
    }

    // ===== the accent =====

    [Fact]
    public void ClassicThemeTakesAccentAsIs()
    {
        Following(() =>
        {
            App.UseSystemAppearance(new FakeAppearance { Palette = new AccentPalette(Colors.Green) });
            App.UseSystemTheme(Themes.Light, Themes.Dark);

            Assert.Equal(Colors.Green, App.Theme.Colors.Accent);
            Assert.Equal(Themes.Light.Name, App.Theme.Name);
        });
    }

    [Fact]
    public void AccentCanBeLeftToTheTheme()
    {
        Following(() =>
        {
            App.UseSystemAppearance(new FakeAppearance { Palette = new AccentPalette(Colors.Green) });
            App.UseSystemTheme(Themes.FluentLight, Themes.FluentDark, followAccent: false);

            Assert.Same(Themes.FluentLight, App.Theme);
        });
    }

    [Fact]
    public void FluentTakesTheSystemShadeForItsPage()
    {
        var dark1 = new Color(0x00, 0x67, 0xC0);
        var light2 = new Color(0x4C, 0xC2, 0xFF);

        // the default Windows palette
        var palette = new AccentPalette(new Color(0x00, 0x78, 0xD4)) { Dark1 = dark1, Light2 = light2 };

        Following(() =>
        {
            var system = new FakeAppearance { Palette = palette };
            App.UseSystemAppearance(system);
            App.UseSystemTheme(Themes.FluentLight, Themes.FluentDark);

            ThemeColors light = App.Theme.Colors;

            Assert.Equal(dark1, light.Accent);
            Assert.Equal(Color.Lerp(light.Background, dark1, 0.9f), light.AccentHover);
            Assert.Equal(Colors.White, light.TextOnAccent);

            system.IsDark = true;
            system.RaiseChanged();

            ThemeColors dark = App.Theme.Colors;

            Assert.Equal(light2, dark.Accent);
            Assert.Equal(Colors.Black, dark.TextOnAccent);

            // still Fluent: the shape came along with the new palette
            Assert.Same(ThemeMetrics.Fluent, App.Theme.Metrics);
        });
    }

    [Fact]
    public void BareAccentGetsReadableShades()
    {
        Color[] accents =
        [
            new(0x00, 0x78, 0xD4),   // the Windows blue
            new(0xFF, 0xC1, 0x07),   // a yellow: white on it is unreadable as it is
            new(0x10, 0x7C, 0x10),   // a green
            new(0x88, 0x17, 0x98),   // a purple
        ];

        foreach (Color accent in accents)
        {
            var palette = new AccentPalette(accent);

            Assert.True(palette.ForLightPage().ContrastRatio(Colors.White) >= 4.5f, accent.ToString());
            Assert.True(palette.ForDarkPage().ContrastRatio(Colors.Black) >= 4.5f, accent.ToString());
        }
    }

    [Fact]
    public void DerivedShadesKeepTheHue()
    {
        var palette = new AccentPalette(new Color(0x00, 0x78, 0xD4));

        Color dark1 = palette.ForLightPage();
        Color light2 = palette.ForDarkPage();

        // blue stays blue: no channel overtakes the blue one
        Assert.True(dark1.B > dark1.G && dark1.G > dark1.R);
        Assert.True(light2.B > light2.G && light2.G > light2.R);

        // and lands near the shades Windows itself computes, 0067C0 and 4CC2FF
        Assert.InRange(dark1.B, 0xB0, 0xD0);
        Assert.InRange(light2.R, 0x30, 0x70);
    }
}