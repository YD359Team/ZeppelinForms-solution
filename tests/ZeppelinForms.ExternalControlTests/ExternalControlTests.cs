using Xunit;
using Xunit.v3;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.ExternalControlTests.Controls;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Headless;
using ZeppelinForms.Skia;
using ZeppelinForms.Theming;

// the tests switch App.Theme and Font.Default, which are global
[assembly: Parallelization(Mode = Xunit.Sdk.ParallelMode.All)]

namespace ZeppelinForms.ExternalControlTests;

/// <summary>
/// Controls of another assembly, built against the public API alone: styled
/// properties, theme rules, style sheets, focus, the input method, flyouts and the
/// Skia canvas — what a package of controls needs from the framework.
/// </summary>
public class ExternalControlTests
{
    private static (Form Form, HeadlessWindow Window) Show(UIElement content)
    {
        var form = new Form { Size = new Size(320, 200), Content = content };
        var window = (HeadlessWindow)new HeadlessPlatform().CreateWindow(form);

        form.UpdateLayout();

        return (form, window);
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

    private static T TopLeft<T>(T element, float width, float height) where T : UIElement
    {
        element.Size = new Size(width, height);
        element.HorizontalAlignment = HorizontalAlignment.Left;
        element.VerticalAlignment = VerticalAlignment.Top;

        return element;
    }

    // ===== styled properties and themes =====

    [Fact]
    public void TheGeneratorWritesStyledPropertiesOfAnotherAssembly()
    {
        var swatch = new Swatch { Fill = new Color(0x12, 0x34, 0x56) };

        Assert.Equal("Fill", Swatch.FillProperty.Name);
        Assert.Equal(new Color(0x12, 0x34, 0x56), swatch.Fill);
        Assert.Equal(new Color(0x12, 0x34, 0x56), Swatch.FillProperty.GetValue(swatch));
    }

    [Fact]
    public void TheGeneratorListsTheAssemblysControlsForStyleSheets()
    {
        // the module initializer that hands the controls to the style sheets: without it
        // a trimmed application couldn't name them, and this project scans nothing else
        Assert.NotNull(typeof(Swatch).Assembly.GetType("ZeppelinForms.Generated.StyleTypeRegistration"));
    }

    [Fact]
    public void TheLibrarysDefaultRuleFollowsTheTheme()
    {
        Theme first = Themes.Light.WithAccent(new Color(0x10, 0x60, 0x20));
        Theme second = Themes.Dark.WithAccent(new Color(0x80, 0x20, 0x90));

        Assert.NotEqual(first.Colors.Accent, second.Colors.Accent);

        WithTheme(first, () =>
        {
            Swatch swatch = TopLeft(new Swatch(), 40, 40);
            Show(swatch);

            Assert.Equal(first.Colors.Accent, swatch.Fill);

            App.Theme = second;

            Assert.Equal(second.Colors.Accent, swatch.Fill);
        });
    }

    [Fact]
    public void AThemesOwnRuleReplacesTheLibrarysDefault()
    {
        Theme custom = Themes.Light.WithAccent(new Color(0x10, 0x60, 0x20))
            .For<Swatch>((swatch, _) => swatch.Fill = Colors.Red);

        WithTheme(custom, () =>
        {
            Swatch swatch = TopLeft(new Swatch(), 40, 40);
            Show(swatch);

            Assert.Equal(Colors.Red, swatch.Fill);
        });

        // the copy has the rule, the built-in theme it came from doesn't
        WithTheme(Themes.Light, () =>
        {
            Swatch swatch = TopLeft(new Swatch(), 40, 40);
            Show(swatch);

            Assert.Equal(Themes.Light.Colors.Accent, swatch.Fill);
        });
    }

    [Fact]
    public void AValueSetInCodeOutlivesTheThemeSwitch()
    {
        WithTheme(Themes.Light, () =>
        {
            Swatch swatch = TopLeft(new Swatch(), 40, 40);
            Show(swatch);

            swatch.Fill = Colors.Green;
            App.Theme = Themes.Dark;

            Assert.Equal(Colors.Green, swatch.Fill);
        });
    }

    [Fact]
    public void AStyleSheetNamesTheControlAndItsPseudoClass()
    {
        WithTheme(Themes.Light, () =>
        {
            Swatch swatch = TopLeft(new Swatch(), 40, 40);
            (Form form, _) = Show(swatch);

            StyleSheet sheet = StyleSheet.Parse("Swatch:chosen { Fill: #102030; }", "external.zss");
            Assert.Empty(sheet.Diagnostics);
            form.Styles.Add(sheet);

            Assert.Equal(Themes.Light.Colors.Accent, swatch.Fill);

            swatch.IsChosen = true;
            Assert.Equal(new Color(0x10, 0x20, 0x30), swatch.Fill);

            swatch.IsChosen = false;
            Assert.Equal(Themes.Light.Colors.Accent, swatch.Fill);
        });
    }

    // ===== drawing =====

    [Fact]
    public void TheSkiaCanvasStandsAtTheElementsPlace()
    {
        WithTheme(Themes.Light, () =>
        {
            Swatch swatch = TopLeft(new Swatch(), 40, 40);
            swatch.Margin = new Thickness(30, 20, 0, 0);
            swatch.Fill = new Color(0x12, 0x34, 0x56);

            (Form form, _) = Show(swatch);

            Image image = new SkiaOffscreenRenderer().RenderForm(
                form, (int)form.ClientSize.Width, (int)form.ClientSize.Height);

            Assert.True(swatch.DrewOnCanvas);

            Point at = swatch.GetAbsolutePosition();
            Assert.True(at.X >= 30 && at.Y >= 20, $"The swatch is at {at}.");

            // drawn at (0, 0) of the canvas, it is inside the swatch on the form;
            // left of the swatch is the window's background
            Assert.Equal(swatch.Fill, PixelAt(image, at.X + 20, at.Y + 20));
            Assert.NotEqual(swatch.Fill, PixelAt(image, at.X - 10, at.Y + 20));
        });
    }

    private static Color PixelAt(Image image, float x, float y)
    {
        int offset = ((int)y * image.Width + (int)x) * 4;

        return new Color(
            image.Pixels[offset + 3],
            image.Pixels[offset],
            image.Pixels[offset + 1],
            image.Pixels[offset + 2]);
    }

    // ===== the form: finding it, focus, flyouts =====

    [Fact]
    public void AnElementFindsItsForm()
    {
        CodeField field = TopLeft(new CodeField(), 200, 30);

        Assert.Null(field.FindForm());

        (Form form, _) = Show(field);

        Assert.Same(form, field.FindForm());
    }

    [Fact]
    public void FocusMovesToTheElement()
    {
        CodeField field = TopLeft(new CodeField(), 200, 30);
        (Form form, _) = Show(field);

        Assert.True(field.Focus());
        Assert.True(field.IsFocused);
        Assert.Same(field, form.FocusedElement);
    }

    [Fact]
    public void FocusIsRefusedOutsideAFormAndWhenDisabled()
    {
        Assert.False(new CodeField().Focus());

        CodeField field = TopLeft(new CodeField(), 200, 30);
        (Form form, _) = Show(field);

        field.IsEnabled = false;

        Assert.False(field.Focus());
        Assert.Null(form.FocusedElement);
    }

    [Fact]
    public void AFlyoutOpensInTheElementsForm()
    {
        CodeField field = TopLeft(new CodeField(), 200, 30);
        (Form form, _) = Show(field);

        var list = new Border { Size = new Size(100, 60) };

        Assert.True(field.OpenSuggestions(list));
        Assert.Same(form, list.FindForm());

        form.CloseFlyout(list);

        Assert.Null(list.FindForm());
        Assert.False(new CodeField().OpenSuggestions(new Border()));
    }

    // ===== text input =====

    [Fact]
    public void TypedTextReachesTheFocusedElement()
    {
        CodeField field = TopLeft(new CodeField(), 200, 30);
        (Form form, _) = Show(field);

        field.Focus();
        HeadlessInput.TypeText(form, "ab");

        Assert.Equal("ab", field.Text);
    }

    [Fact]
    public void TheCompositionIsShownInPlaceAndCommitted()
    {
        CodeField field = TopLeft(new CodeField(), 200, 30);
        (Form form, _) = Show(field);

        field.Focus();

        HeadlessInput.Compose(form, "にほ", 1);

        Assert.Equal("にほ", field.Composition);
        Assert.Equal(1, field.CompositionCursor);
        Assert.Equal(string.Empty, field.Text);

        HeadlessInput.CommitComposition(form, "日本");

        Assert.Equal(string.Empty, field.Composition);
        Assert.Equal("日本", field.Text);
    }

    [Fact]
    public void TheInputMethodFollowsTheReportedCaret()
    {
        WithTheme(Themes.Light, () =>
        {
            CodeField field = TopLeft(new CodeField(), 200, 30);
            field.Margin = new Thickness(10, 15, 0, 0);

            (Form form, HeadlessWindow window) = Show(field);

            field.Focus();
            HeadlessInput.TypeText(form, "abc");

            // drawn: the caret is reported as the field draws it
            new SkiaOffscreenRenderer().RenderForm(
                form, (int)form.ClientSize.Width, (int)form.ClientSize.Height);

            Point at = field.GetAbsolutePosition();
            Rectangle expected = field.Caret.Offset(at.X, at.Y);

            Assert.Equal<Rectangle?>(expected, window.TextInputArea);
            Assert.Equal(at.X + 4 + 3 * CodeField.CharWidth, expected.X, 0.001f);
        });
    }
}