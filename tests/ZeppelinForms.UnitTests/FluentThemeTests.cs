using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;
using ZeppelinForms.Theming;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// The Fluent themes of 0.13.0 and the theme switch they need: what a theme stops
/// setting goes back to the control's default. Tests that switch App.Theme restore
/// both it and Font.Default — the theme setter replaces the default font.
/// </summary>
[Collection("Platform")]
public class FluentThemeTests
{
    private static Form CreateForm(UIElement content)
    {
        var form = new Form { Size = new Size(240, 80), Content = content };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

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

    public static IEnumerable<Theme> FluentThemes => [Themes.FluentLight, Themes.FluentDark];

    // ===== palettes =====

    [Fact]
    public void ClassicThemesKeepClassicShapes()
    {
        Assert.Same(ThemeMetrics.Default, Themes.Light.Metrics);
        Assert.Same(ThemeMetrics.Default, Themes.Dark.Metrics);
        Assert.Same(ThemeMetrics.Fluent, Themes.FluentLight.Metrics);
        Assert.Same(ThemeMetrics.Fluent, Themes.FluentDark.Metrics);
    }

    [Fact]
    public void FluentPalettesAreReadable()
    {
        foreach (Theme theme in FluentThemes)
        {
            ThemeColors c = theme.Colors;

            // WCAG: 7:1 for body text is AAA, 4.5:1 is AA
            Assert.True(c.Text.ContrastRatio(c.Background) >= 7f, theme.Name);
            Assert.True(c.TextSecondary.ContrastRatio(c.Background) >= 4.5f, theme.Name);
            Assert.True(c.TextOnAccent.ContrastRatio(c.Accent) >= 4.5f, theme.Name);
        }

        Assert.False(Themes.FluentLight.Colors.IsDark);
        Assert.True(Themes.FluentDark.Colors.IsDark);
    }

    [Fact]
    public void StrongStrokeFallsBackToSecondaryText()
    {
        ThemeColors c = Themes.Light.Colors;

        Assert.Equal(c.TextSecondary, c.ControlStrongStroke);
    }

    // ===== controls =====

    [Fact]
    public void FluentButtonTakesShapeAndFocusTokens()
    {
        WithTheme(Themes.FluentLight, () =>
        {
            var button = new Button { Text = "x" };
            CreateForm(button);

            ThemeColors c = Themes.FluentLight.Colors;

            Assert.Equal(new CornerRadius(4f), button.CornerRadius);
            Assert.Equal(c.ControlStrokeSecondary, button.ElevationBorderColor);
            Assert.False(button.RippleEnabled);

            Assert.Equal(c.FocusStrokeOuter, button.FocusRingColor);
            Assert.Equal(c.FocusStrokeInner, button.FocusRingInnerColor);
            Assert.Equal(2f, button.FocusRingThickness);
            Assert.Equal(1f, button.FocusRingInnerThickness);
            Assert.Equal(1f, button.FocusRingInset);
        });
    }

    [Fact]
    public void FluentCheckBoxGlyphReadsOnLightAccent()
    {
        WithTheme(Themes.FluentDark, () =>
        {
            var check = new CheckBox { Text = "x", IsChecked = true };
            CreateForm(check);

            Assert.Equal(20f, check.BoxSize);

            // the dark accent is a light blue: the glyph on it is black
            Assert.Equal(Colors.Black, check.CheckGlyphColor);
        });
    }

    [Fact]
    public void FluentFieldShowsFocusByUnderline()
    {
        WithTheme(Themes.FluentLight, () =>
        {
            var box = new TextBox();
            var combo = new ComboBox();

            CreateForm(box);
            CreateForm(combo);

            ThemeColors c = Themes.FluentLight.Colors;

            Assert.Equal(c.Accent, box.FocusUnderlineColor);
            Assert.Equal(c.ControlStrongStroke, box.UnderlineColor);
            Assert.Equal(Colors.Transparent, box.FocusBorderColor);

            // no ring and no underline of its own: the border shows keyboard focus
            Assert.Equal(c.FocusStrokeOuter, combo.FocusBorderColor);
        });
    }

    [Fact]
    public void OverlaysAreRoundedMoreThanControls()
    {
        WithTheme(Themes.FluentLight, () =>
        {
            var menu = new MenuList();
            CreateForm(menu);

            Assert.Equal(new CornerRadius(8f), menu.CornerRadius);
        });
    }

    [Fact]
    public void ClassicGlyphFollowsTextOnAccent()
    {
        // white on yellow is unreadable, so WithAccent switches the text to black
        Theme yellow = Themes.Light.WithAccent(new Color(0xFF, 0xC1, 0x07));

        WithTheme(yellow, () =>
        {
            var check = new CheckBox { IsChecked = true };
            CreateForm(check);

            Assert.Equal(Colors.Black, check.CheckGlyphColor);
        });
    }

    [Fact]
    public void RecoloredFluentKeepsItsShape()
    {
        Theme green = Themes.FluentDark.WithAccent(Colors.Green);

        WithTheme(green, () =>
        {
            var primary = new PrimaryButton { Text = "x" };
            CreateForm(primary);

            Assert.Equal(Colors.Green, primary.BackgroundColor);
            Assert.Equal(new CornerRadius(4f), primary.CornerRadius);
            Assert.False(primary.RippleEnabled);
        });
    }

    // ===== theme switch =====

    [Fact]
    public void SwitchingBackWithdrawsFluentValues()
    {
        var check = new CheckBox { Text = "x" };
        var button = new Button { Text = "x" };
        var panel = new StackPanel();
        panel.Children.Add(check);
        panel.Children.Add(button);

        WithTheme(Themes.FluentLight, () =>
        {
            CreateForm(panel);

            Assert.Equal(20f, check.BoxSize);
            Assert.False(button.RippleEnabled);

            App.Theme = Themes.Light;

            // what Light doesn't set goes back to the control's own default…
            Assert.Equal(16f, check.BoxSize);
            Assert.Equal(1.5f, check.FocusRingThickness);
            Assert.Equal(new Thickness(14, 6), button.Padding);

            // …or to the property's, where the control has none
            Assert.True(button.RippleEnabled);
            Assert.Equal(Colors.Transparent, button.ElevationBorderColor);
            Assert.Equal(1f, button.FocusRingThickness);

            // and what Light does set is Light's
            Assert.Equal(Themes.Light.Colors.Accent, check.CheckColor);
        });
    }

    [Fact]
    public void SwitchKeepsValuesSetInCode()
    {
        var check = new CheckBox { Text = "x", BoxSize = 24f };

        WithTheme(Themes.FluentLight, () =>
        {
            CreateForm(check);
            Assert.Equal(24f, check.BoxSize);

            App.Theme = Themes.Light;
            Assert.Equal(24f, check.BoxSize);
        });
    }

    [Fact]
    public void ClearValueReturnsToControlDefault()
    {
        var button = new Button { Text = "x", Padding = new Thickness(1) };
        CreateForm(button);

        button.ClearValue(UIElement.PaddingProperty);

        // the classic themes don't set a button's padding: the button's own 14×6
        // is what is left, not the property's zero
        Assert.Equal(new Thickness(14, 6), button.Padding);
        Assert.False(button.IsLocal(UIElement.PaddingProperty));
    }

    [Fact]
    public void EveryControlStylesAndDrawsUnderFluentAndBack()
    {
        HeadlessElementRenderer.Register();

        foreach (Type type in typeof(UIElement).Assembly.GetTypes())
        {
            if (type.IsAbstract || !type.IsPublic) continue;
            if (!typeof(UIElement).IsAssignableFrom(type)) continue;
            if (type.GetConstructor(Type.EmptyTypes) is null) continue;

            // the same exclusions as DrawSmokeTests: the network, a Grid parent
            if (type.Name is "MapControl" or "GridSplitter") continue;

            foreach (Theme theme in FluentThemes)
            {
                WithTheme(theme, () =>
                {
                    var control = (UIElement)Activator.CreateInstance(type)!;
                    var form = new Form { Size = new Size(200, 100), Content = control };
                    new HeadlessPlatform(registerServices: true).CreateWindow(form);

                    ElementRenderer.Current.Render(control, 200, 100);

                    // and back: every value the Fluent rules wrote is withdrawn
                    App.Theme = Themes.Dark;
                    ElementRenderer.Current.Render(control, 200, 100);
                });
            }
        }
    }
}