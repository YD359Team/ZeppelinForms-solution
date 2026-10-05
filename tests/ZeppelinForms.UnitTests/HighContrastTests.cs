using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Headless;
using ZeppelinForms.Theming;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// The system's high contrast over any theme: built from the user's own colors,
/// winning over a theme chosen in code, giving way when turned off. The system is
/// a fake the test drives; every test disconnects it and restores the theme.
/// </summary>
[Collection("Platform")]
public class HighContrastTests
{
    private sealed class FakeAppearance : ISystemAppearance
    {
        public bool IsDark { get; set; }

        public Color? AccentColor => null;

        public bool IsHighContrast { get; set; }

        public HighContrastPalette? HighContrastPalette { get; set; }

        public event EventHandler? Changed;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void Restoring(Action test)
    {
        Theme themeBefore = App.RequestedTheme;
        Font fontBefore = Font.Default;

        try
        {
            test();
        }
        finally
        {
            App.UseSystemAppearance(null);
            App.RespectHighContrast = true;
            App.Theme = themeBefore;
            Font.Default = fontBefore;
        }
    }

    private static Form CreateForm(UIElement content)
    {
        var form = new Form { Size = new Size(300, 200), Content = content };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    // ===== precedence =====

    [Fact]
    public void ContrastWinsOverThemeChosenInCode()
    {
        Restoring(() =>
        {
            App.Theme = Themes.FluentLight;
            App.UseSystemAppearance(new FakeAppearance { IsHighContrast = true, IsDark = true });

            Assert.True(App.IsHighContrastActive);
            Assert.Equal("HighContrast", App.Theme.Name);

            // what the application asked for is kept, not forgotten
            Assert.Same(Themes.FluentLight, App.RequestedTheme);
        });
    }

    [Fact]
    public void TurningContrastOffBringsTheRequestedThemeBack()
    {
        Restoring(() =>
        {
            var system = new FakeAppearance { IsHighContrast = true };
            App.Theme = Themes.FluentDark;
            App.UseSystemAppearance(system);

            system.IsHighContrast = false;
            system.RaiseChanged();

            Assert.Same(Themes.FluentDark, App.Theme);
        });
    }

    [Fact]
    public void ApplicationMayKeepItsOwnLook()
    {
        Restoring(() =>
        {
            App.Theme = Themes.Light;
            App.RespectHighContrast = false;
            App.UseSystemAppearance(new FakeAppearance { IsHighContrast = true });

            Assert.False(App.IsHighContrastActive);
            Assert.Same(Themes.Light, App.Theme);

            App.RespectHighContrast = true;
            Assert.Equal("HighContrast", App.Theme.Name);
        });
    }

    // ===== palette =====

    [Fact]
    public void TheUsersOwnColorsAreUsed()
    {
        var navy = HighContrastPalette.Black with { Window = new Color(0, 0, 0x40), ButtonFace = new Color(0, 0, 0x40) };

        Restoring(() =>
        {
            App.UseSystemAppearance(new FakeAppearance { IsHighContrast = true, HighContrastPalette = navy });

            Assert.Equal(navy.Window, App.Theme.Colors.Background);
        });
    }

    [Fact]
    public void WithoutColorsTheModePicksThePreset()
    {
        Restoring(() =>
        {
            var system = new FakeAppearance { IsHighContrast = true, IsDark = false };
            App.UseSystemAppearance(system);

            Assert.Equal(HighContrastPalette.White.Window, App.Theme.Colors.Background);

            system.IsDark = true;
            system.RaiseChanged();

            Assert.Equal(HighContrastPalette.Black.Window, App.Theme.Colors.Background);
        });
    }

    [Fact]
    public void SameColorsKeepTheSameTheme()
    {
        Restoring(() =>
        {
            var system = new FakeAppearance { IsHighContrast = true };
            App.UseSystemAppearance(system);

            Theme first = App.Theme;
            int switches = 0;
            EventHandler count = (_, _) => switches++;
            App.ThemeChanged += count;

            try
            {
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

    [Fact]
    public void PresetsAreReadable()
    {
        foreach (HighContrastPalette p in new[] { HighContrastPalette.Black, HighContrastPalette.White })
        {
            Assert.True(p.WindowText.ContrastRatio(p.Window) >= 7f);
            Assert.True(p.ButtonText.ContrastRatio(p.ButtonFace) >= 7f);
            Assert.True(p.HighlightText.ContrastRatio(p.Highlight) >= 4.5f);
            Assert.True(p.GrayText.ContrastRatio(p.Window) >= 4.5f);
        }
    }

    [Fact]
    public void ContrastTakesNoAccent()
    {
        Theme recolored = Themes.HighContrastBlack.WithAccent(Colors.Green);

        Assert.Equal(HighContrastPalette.Black.Highlight, recolored.Colors.Accent);
    }

    // ===== controls =====

    [Fact]
    public void AllButtonsLookAlike()
    {
        Restoring(() =>
        {
            App.Theme = Themes.HighContrastBlack;

            var primary = new PrimaryButton { Text = "OK" };
            CreateForm(primary);

            HighContrastPalette p = HighContrastPalette.Black;

            Assert.Equal(p.ButtonFace, primary.BackgroundColor);
            Assert.Equal(p.ButtonText, primary.TextColor);
            Assert.Equal(2f, primary.FocusRingThickness);
            Assert.False(primary.RippleEnabled);
        });
    }

    [Fact]
    public void SelectedRowTakesTheHighlightPair()
    {
        Restoring(() =>
        {
            App.Theme = Themes.HighContrastBlack;

            var list = new ListBox();
            list.Items.Add("One");
            list.Items.Add("Two");
            CreateForm(list);

            HighContrastPalette p = HighContrastPalette.Black;

            list.SelectedIndex = 1;
            Assert.Equal(p.HighlightText, list.Children[1].TextColor);

            // left the selection: back to the theme's own color
            list.SelectedIndex = 0;
            Assert.Equal(p.WindowText, list.Children[1].TextColor);
            Assert.Equal(p.HighlightText, list.Children[0].TextColor);
        });
    }

    [Fact]
    public void ClassicThemesLeaveSelectedTextAlone()
    {
        Restoring(() =>
        {
            App.Theme = Themes.Light;

            var list = new ListBox();
            list.Items.Add("One");
            CreateForm(list);

            list.SelectedIndex = 0;

            Assert.Equal(Themes.Light.Colors.Text, list.Children[0].TextColor);
        });
    }
}