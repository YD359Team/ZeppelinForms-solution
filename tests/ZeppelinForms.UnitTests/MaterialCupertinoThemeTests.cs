using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Theming;

namespace ZeppelinForms.UnitTests;

/// <summary>0.20: the Material 3 themes — a scheme made from a seed, as Material You makes
/// it — and the Cupertino ones, iOS's semantic colors. Tests that switch App.Theme restore it
/// and Font.Default, which the theme setter replaces.</summary>
[Collection("Platform")]
public class MaterialCupertinoThemeTests
{
    public static IEnumerable<Theme> NewThemes =>
        [Themes.MaterialLight, Themes.MaterialDark, Themes.CupertinoLight, Themes.CupertinoDark];

    private static void WithTheme(Theme theme, Action test)
    {
        Theme themeBefore = App.Theme;
        Font fontBefore = Font.Default;

        try
        {
            App.Theme = theme;
            test();
        }
        finally
        {
            App.Theme = themeBefore;
            Font.Default = fontBefore;
        }
    }

    private static Form Show(UIElement content)
    {
        var form = new Form { Size = new Size(320, 200), Content = content };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    private static int Distance(Color a, Color b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    // ===== Material =====

    /// <summary>The baseline seed gives Material's baseline scheme, within a few steps of
    /// each channel: hue and chroma are LCh's here, CAM16's in Material.</summary>
    [Fact]
    public void TheBaselineSeedGivesMaterialsBaselineScheme()
    {
        ThemeColors light = Themes.MaterialLight.Colors;
        ThemeColors dark = Themes.MaterialDark.Colors;

        (Color Actual, uint Spec)[] roles =
        [
            (light.Accent, 0x6750A4),          // primary
            (light.TextOnAccent, 0xFFFFFF),    // on primary
            (light.Selection, 0xE8DEF8),       // secondary container
            (light.Background, 0xFEF7FF),      // surface
            (light.Surface, 0xF3EDF7),         // surface container
            (light.Text, 0x1D1B20),            // on surface
            (light.TextSecondary, 0x49454F),   // on surface variant
            (light.ControlStroke, 0x79747E),   // outline
            (light.Border, 0xCAC4D0),          // outline variant
            (dark.Accent, 0xD0BCFF),
            (dark.TextOnAccent, 0x381E72),
            (dark.Selection, 0x4A4458),
            (dark.Background, 0x141218),
            (dark.Surface, 0x211F26),
            (dark.Text, 0xE6E0E9),
            (dark.ControlStroke, 0x938F99),
        ];

        foreach ((Color actual, uint spec) in roles)
        {
            var expected = new Color(255, (byte)(spec >> 16), (byte)(spec >> 8), (byte)spec);
            Assert.True(Distance(actual, expected) <= 8, $"{actual} is not near #{spec:X6}");
        }
    }

    [Fact]
    public void AnAccentIsASeedOfAWholeScheme()
    {
        var green = new Color(255, 0x19, 0x87, 0x54);
        Theme theme = Themes.MaterialLight.WithAccent(green);
        ThemeColors c = theme.Colors;

        // the primary is a green of tone 40, and the surfaces are tinted by it too
        Assert.True(c.Accent.G > c.Accent.R && c.Accent.G > c.Accent.B, c.Accent.ToString());
        Assert.NotEqual(Themes.MaterialLight.Colors.Background, c.Background);
        Assert.True(Distance(c.Background, Themes.MaterialLight.Colors.Background) <= 12, "a surface stays near white");
        Assert.True(c.TextOnAccent.ContrastRatio(c.Accent) >= 4.5f);

        // the rules stay the Material ones
        Assert.Same(ThemeMetrics.Material, theme.Metrics);
        Assert.Same(TypeRamp.Material, theme.TypeRamp);

        // and in the dark one the primary is light, under dark text
        ThemeColors dark = Themes.MaterialDark.WithAccent(green).Colors;
        Assert.True(dark.IsDark);
        Assert.True(dark.Accent.ContrastRatio(Colors.Black) > dark.Accent.ContrastRatio(Colors.White));
    }

    [Fact]
    public void AButtonIsAPillAndAnIconButtonKeepsItsShape()
    {
        WithTheme(Themes.MaterialLight, () =>
        {
            var button = new Button { Text = "Save" };
            var primary = new PrimaryButton { Text = "Send" };
            var icon = new AppBarButton(IconSource.FromPath("M0 0h1v1z"), "Star");
            Show(new StackPanel { Children = { button, primary, icon } });

            Assert.Equal(new Thickness(24, 10), button.Padding);
            Assert.True(button.CornerRadius.TopLeft >= button.ActualSize.Height / 2f, "a pill");
            Assert.Equal(Themes.MaterialLight.Colors.Selection, button.BackgroundColor);
            Assert.True(button.RippleEnabled);

            Assert.Equal(Themes.MaterialLight.Colors.Accent, primary.BackgroundColor);
            Assert.NotEqual(new Thickness(24, 10), icon.Padding);
        });
    }

    [Fact]
    public void TheTypeScaleIsMaterials()
    {
        WithTheme(Themes.MaterialLight, () =>
        {
            var title = new Label { Text = "Title", TextStyle = TextStyle.Title };
            var label = new Label { Text = "Label", TextStyle = TextStyle.BodyStrong };
            Show(new StackPanel { Children = { title, label } });

            Assert.Equal(28f, title.EffectiveFont.Size);
            Assert.Equal(FontWeight.Medium, label.EffectiveFont.Weight);
            Assert.StartsWith("Roboto", label.EffectiveFont.Family);
        });
    }

    // ===== Cupertino =====

    [Fact]
    public void TheSwitchIsGreenWhateverTheAccent()
    {
        Theme purple = Themes.CupertinoLight.WithAccent(new Color(255, 0xAF, 0x52, 0xDE));

        WithTheme(purple, () =>
        {
            var toggle = new ToggleSwitch { IsOn = true };
            var box = new TextBox();
            Show(new StackPanel { Children = { toggle, box } });

            Assert.Equal(Themes.CupertinoLight.Colors.Success, toggle.OnColor);
            Assert.Equal(new Size(51f, 31f), toggle.TrackSize);

            // fields are fills: no outline, no underline
            Assert.Equal(Themes.CupertinoLight.Colors.ControlFill, box.Background);
            Assert.Equal(0, box.BorderColor.A);
            Assert.Equal(0, box.UnderlineColor.A);
        });

        // a tint doesn't touch the grey of a selected row
        Assert.Equal(Themes.CupertinoLight.Colors.Selection, purple.Colors.Selection);
        Assert.Equal(new Color(255, 0xAF, 0x52, 0xDE), purple.Colors.Accent);
    }

    [Fact]
    public void ABodyIsSeventeenAsOnAnIPhone()
    {
        Assert.Equal(17f, Themes.CupertinoLight.TypeRamp.Body.Size);
        Assert.Equal(17f, Themes.CupertinoLight.BaseFont.Size);
        Assert.Equal(FontWeight.Bold, Themes.CupertinoDark.TypeRamp.TitleLarge.Weight);
    }

    // ===== both =====

    [Fact]
    public void ThePalettesReadAsTheirSystemsMadeThem()
    {
        foreach (Theme theme in NewThemes)
        {
            ThemeColors c = theme.Colors;

            Assert.True(c.Text.ContrastRatio(c.Background) >= 7f, theme.Name);
            Assert.True(c.Text.ContrastRatio(c.Surface) >= 7f, theme.Name);

            // Material's tones are four steps apart, 4.5:1 at least; iOS's white on its blue
            // is 4:1, Apple's own choice, readable at the label sizes of iOS
            Assert.True(c.TextOnAccent.ContrastRatio(c.Accent) >= (theme.Name.StartsWith("Material", StringComparison.Ordinal) ? 4.5f : 3.5f), theme.Name);
        }

        Assert.True(Themes.MaterialDark.Colors.IsDark);
        Assert.True(Themes.CupertinoDark.Colors.IsDark);
        Assert.False(Themes.MaterialLight.Colors.IsDark);
        Assert.False(Themes.CupertinoLight.Colors.IsDark);
    }

    /// <summary>Every control is drawn in each of the four, and gives back what they set
    /// when the theme goes back to another.</summary>
    [Fact]
    public void EveryControlDrawsInEachAndGoesBack()
    {
        foreach (Type type in typeof(UIElement).Assembly.GetTypes())
        {
            if (type.IsAbstract || !type.IsPublic) continue;
            if (!typeof(UIElement).IsAssignableFrom(type)) continue;
            if (type.GetConstructor(Type.EmptyTypes) is null) continue;

            // the same exclusions as DrawSmokeTests: the network, a Grid parent
            if (type.Name is "MapControl" or "GridSplitter") continue;

            foreach (Theme theme in NewThemes)
            {
                WithTheme(theme, () =>
                {
                    var control = (UIElement)Activator.CreateInstance(type)!;
                    var form = new Form { Size = new Size(200, 100), Content = control };
                    new HeadlessPlatform(registerServices: true).CreateWindow(form);

                    ElementRenderer.Current.Render(control, 200, 100);

                    App.Theme = Themes.Dark;
                    ElementRenderer.Current.Render(control, 200, 100);
                });
            }
        }
    }

    [Fact]
    public void ShapesGoBackWhenTheThemeChanges()
    {
        WithTheme(Themes.CupertinoLight, () =>
        {
            var box = new CheckBox { Text = "Done" };
            var button = new Button { Text = "OK" };
            Show(new StackPanel { Children = { box, button } });

            Assert.Equal(22f, box.BoxSize);
            Assert.Equal(new Thickness(16, 8), button.Padding);

            App.Theme = Themes.Light;

            Assert.NotEqual(22f, box.BoxSize);
            Assert.Equal(new Thickness(14, 6), button.Padding);
        });
    }
}
