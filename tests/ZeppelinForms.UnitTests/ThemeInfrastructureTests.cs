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

/// <summary>
/// The theme infrastructure of 0.13.0 that Fluent is built on: rules that get the
/// whole theme, shape tokens, optional color roles, the accent swap and the type ramp.
/// Tests that switch App.Theme restore both it and Font.Default: the theme setter
/// replaces the default font, and the snapshot tests in this collection depend on it.
/// </summary>
[Collection("Platform")]
public class ThemeInfrastructureTests
{
    private static Form CreateForm(UIElement content)
    {
        var form = new Form { Size = new Size(400, 300), Content = content };

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

    private static ThemeColors Palette => Themes.Light.Colors;

    // ===== rules =====

    [Fact]
    public void ColorsOverloadStillStyles()
    {
        Theme theme = new Theme { Name = "test.colors", Colors = Palette }
            .For<Button>((button, c) => button.BackgroundColor = c.Error);

        WithTheme(theme, () =>
        {
            var button = new Button { Text = "x" };
            CreateForm(button);

            Assert.Equal(Palette.Error, button.BackgroundColor);
        });
    }

    [Fact]
    public void ThemeOverloadReceivesShapeTokens()
    {
        var radius = new CornerRadius(7f);

        Theme theme = new Theme
        {
            Name = "test.metrics",
            Colors = Palette,
            Metrics = ThemeMetrics.Default with { ControlCornerRadius = radius },
        }
        .For<Button>((button, t) => button.CornerRadius = t.Metrics.ControlCornerRadius);

        WithTheme(theme, () =>
        {
            var button = new Button { Text = "x" };
            CreateForm(button);

            Assert.Equal(radius, button.CornerRadius);
        });
    }

    [Fact]
    public void LambdaIgnoringSecondParameterCompiles()
    {
        // fits both overloads; without the priority attribute this line
        // is CS0121 and the whole test project doesn't build
        Theme theme = new Theme { Name = "test.discard", Colors = Palette }
            .For<Button>((button, _) => button.BackgroundColor = Colors.Red);

        WithTheme(theme, () =>
        {
            var button = new Button { Text = "x" };
            CreateForm(button);

            Assert.Equal(Colors.Red, button.BackgroundColor);
        });
    }

    [Fact]
    public void CopyWithOtherColorsPaintsWithItsOwnPalette()
    {
        Theme original = new Theme { Name = "test.copy", Colors = Palette }
            .For<Button>((button, c) => button.BackgroundColor = c.Accent);

        Theme copy = original.WithAccent(Colors.Green);

        WithTheme(copy, () =>
        {
            var button = new Button { Text = "x" };
            CreateForm(button);

            // a rule that captured the original theme would paint the old accent
            Assert.Equal(Colors.Green, button.BackgroundColor);
        });
    }

    [Fact]
    public void CopyIsIndependentOfOriginal()
    {
        Theme original = new Theme { Name = "test.independent", Colors = Palette }
            .For<Button>((button, c) => button.BackgroundColor = c.Accent);

        Theme copy = original.WithColors(Palette, "test.independent.copy");

        // added to the original after the copy was made
        original.For<Button>((button, c) => button.BackgroundColor = c.Error);

        WithTheme(copy, () =>
        {
            var button = new Button { Text = "x" };
            CreateForm(button);

            Assert.Equal(Palette.Accent, button.BackgroundColor);
        });

        Assert.Equal("test.independent.copy", copy.Name);
        Assert.Same(original.Metrics, copy.Metrics);
        Assert.Same(original.TypeRamp, copy.TypeRamp);
    }

    // ===== colors =====

    [Fact]
    public void OptionalRolesFallBackToRequiredOnes()
    {
        ThemeColors c = Palette;

        Assert.Equal(c.Surface, c.ControlFill);
        Assert.Equal(c.Border, c.ControlStroke);
        Assert.Equal(c.ControlStroke, c.ControlStrokeSecondary);
        Assert.Equal(c.Text, c.FocusStrokeOuter);
        Assert.Equal(c.Background, c.FocusStrokeInner);
    }

    [Fact]
    public void UnsetOptionalRoleFollowsItsBaseThroughWith()
    {
        ThemeColors c = Palette with { Surface = Colors.Red };

        Assert.Equal(Colors.Red, c.ControlFill);
    }

    [Fact]
    public void SetOptionalRoleSurvivesWith()
    {
        ThemeColors c = Palette with { ControlFill = Colors.Blue };
        ThemeColors changed = c with { Surface = Colors.Red };

        Assert.Equal(Colors.Blue, changed.ControlFill);
    }

    [Fact]
    public void DarknessIsReadFromBackground()
    {
        Assert.False(Themes.Light.Colors.IsDark);
        Assert.True(Themes.Dark.Colors.IsDark);
    }

    [Fact]
    public void AccentShadesFollowPaletteDirection()
    {
        var accent = new Color(0x10, 0x7C, 0x10);

        ThemeColors light = Themes.Light.Colors.WithAccent(accent);
        ThemeColors dark = Themes.Dark.Colors.WithAccent(accent);

        Assert.Equal(accent, light.Accent);
        Assert.True(light.AccentHover.RelativeLuminance() < accent.RelativeLuminance());
        Assert.True(light.AccentPressed.RelativeLuminance() < light.AccentHover.RelativeLuminance());

        Assert.True(dark.AccentHover.RelativeLuminance() > accent.RelativeLuminance());
        Assert.True(dark.AccentPressed.RelativeLuminance() < accent.RelativeLuminance());
    }

    [Fact]
    public void FocusBorderFollowsAccentOnlyWhereItWasTheAccent()
    {
        ThemeColors own = Palette with { BorderFocused = Colors.Red };

        Assert.Equal(Colors.Green, Palette.WithAccent(Colors.Green).BorderFocused);
        Assert.Equal(Colors.Red, own.WithAccent(Colors.Green).BorderFocused);
    }

    [Fact]
    public void TextOnAccentSwitchesOnlyWhenUnreadable()
    {
        // white on a medium blue is readable — it is kept
        Assert.Equal(Colors.White, Palette.WithAccent(new Color(0x0D, 0x6E, 0xFD)).TextOnAccent);

        // white on yellow is not — it becomes black
        Assert.Equal(Colors.Black, Palette.WithAccent(new Color(0xFF, 0xC1, 0x07)).TextOnAccent);
    }

    [Fact]
    public void ContrastRatioSpansOneToTwentyOne()
    {
        Assert.Equal(21f, Colors.Black.ContrastRatio(Colors.White), 2);
        Assert.Equal(1f, Colors.Red.ContrastRatio(Colors.Red), 3);
    }

    // ===== typography =====

    [Fact]
    public void FontWeightsAreNumeric()
    {
        Assert.Equal(300, (int)FontWeight.Light);
        Assert.Equal(400, (int)FontWeight.Normal);
        Assert.Equal(600, (int)FontWeight.SemiBold);
        Assert.Equal(700, (int)FontWeight.Bold);

        Assert.Equal(FontWeight.SemiBold, new Font("Arial", 12).SemiBold().Weight);
    }

    [Fact]
    public void TextStyleTakesSizeAndWeightFromRamp()
    {
        WithTheme(Themes.Light, () =>
        {
            var label = new Label { Text = "x", TextStyle = TextStyle.Title };
            CreateForm(label);

            Font font = label.EffectiveFont;

            Assert.Equal(TypeRamp.Default.Title.Size, font.Size);
            Assert.Equal(FontWeight.SemiBold, font.Weight);

            // the family is the element's own, not the ramp's
            Assert.Equal(Font.Default.Family, font.Family);
        });
    }

    [Fact]
    public void OwnFontIsStrongerThanTextStyle()
    {
        WithTheme(Themes.Light, () =>
        {
            var own = new Font("Arial", 11);
            var label = new Label { Text = "x", TextStyle = TextStyle.Display, Font = own };
            CreateForm(label);

            Assert.Equal(own, label.EffectiveFont);
        });
    }

    [Fact]
    public void TextStyleFollowsThemeRamp()
    {
        Theme small = new Theme
        {
            Name = "test.ramp",
            Colors = Palette,
            TypeRamp = TypeRamp.Default with { Title = new TypeRampStep(17f, FontWeight.Bold) },
        };

        var label = new Label { Text = "x", TextStyle = TextStyle.Title };

        WithTheme(Themes.Light, () =>
        {
            CreateForm(label);
            Assert.Equal(TypeRamp.Default.Title.Size, label.EffectiveFont.Size);

            // same base font instance, another ramp: the cached font must not survive
            App.Theme = small;

            Assert.Equal(17f, label.EffectiveFont.Size);
            Assert.Equal(FontWeight.Bold, label.EffectiveFont.Weight);
        });
    }

    [Fact]
    public void TextStyleChangesMeasuredSize()
    {
        WithTheme(Themes.Light, () =>
        {
            var label = new Label
            {
                Text = "x",
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };

            Form form = CreateForm(label);
            float before = label.DesiredSize.Height;

            label.TextStyle = TextStyle.Display;
            form.UpdateLayout();

            Assert.True(label.DesiredSize.Height > before);
        });
    }

    [Fact]
    public void RampHasNoStepForNone()
    {
        Assert.Null(TypeRamp.Default[TextStyle.None]);

        var font = new Font("Arial", 11);
        Assert.Same(font, TypeRamp.Default.Apply(font, TextStyle.None));
    }
}