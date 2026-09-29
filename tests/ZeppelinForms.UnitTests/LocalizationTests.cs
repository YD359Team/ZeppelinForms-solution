using System.Globalization;
using Xunit;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// Every test uses keys of its own: tables stay registered for the rest of the
/// run, and a shared key would make one test's table leak into another's result.
/// </summary>
[Collection("Platform")]
public class LocalizationTests
{
    private static void WithCulture(string name, Action test)
    {
        CultureInfo before = Localization.Culture;

        try
        {
            Localization.Culture = new CultureInfo(name);
            test();
        }
        finally
        {
            Localization.Culture = before;
        }
    }

    [Fact]
    public void SpecificCultureFindsNeutralTable()
    {
        var key = new TextKey("test.chain", "Hello");
        Localization.Register(new StringTable("ru").Add(key, "Привет"));

        WithCulture("ru-RU", () => Assert.Equal("Привет", Localization.Get(key)));
    }

    [Fact]
    public void CultureWithoutTableFallsBackToEnglish()
    {
        var key = new TextKey("test.fallback", "Hello");
        Localization.Register(new StringTable("ru").Add(key, "Привет"));

        WithCulture("de-DE", () => Assert.Equal("Hello", Localization.Get(key)));
    }

    [Fact]
    public void LaterTableWins()
    {
        var key = new TextKey("test.override", "OK");
        Localization.Register(new StringTable("ru").Add(key, "ОК"));
        Localization.Register(new StringTable("ru").Add(key, "Хорошо"));

        WithCulture("ru-RU", () => Assert.Equal("Хорошо", Localization.Get(key)));
    }

    [Fact]
    public void ArgumentsAreFormattedByTheCulture()
    {
        var key = new TextKey("test.format", "Total: {0:N1}");

        WithCulture("ru-RU", () => Assert.Equal("Total: 1 234,5", Localization.Get(key, 1234.5)
            .Replace('\u00A0', ' ').Replace('\u202F', ' ')));
    }

    [Theory]
    [InlineData(1, "1 файл")]
    [InlineData(2, "2 файла")]
    [InlineData(5, "5 файлов")]
    [InlineData(11, "11 файлов")]
    [InlineData(12, "12 файлов")]
    [InlineData(21, "21 файл")]
    [InlineData(22, "22 файла")]
    [InlineData(112, "112 файлов")]
    public void RussianPluralForms(long count, string expected)
    {
        var key = new PluralKey("test.plural.ru", one: "{0} file", other: "{0} files");
        Localization.Register(new StringTable("ru").Add(key, one: "{0} файл", few: "{0} файла", many: "{0} файлов"));

        WithCulture("ru-RU", () => Assert.Equal(expected, Localization.Get(key, count)));
    }

    [Theory]
    [InlineData(1, "1 file")]
    [InlineData(2, "2 files")]
    [InlineData(0, "0 files")]
    public void EnglishPluralFallbackUsesTheEnglishRule(long count, string expected)
    {
        var key = new PluralKey("test.plural.en", one: "{0} file", other: "{0} files");

        // no Russian table: the English forms must go by the English rule,
        // not end up with "2 file" through Russian categories
        WithCulture("ru-RU", () => Assert.Equal(expected, Localization.Get(key, count)));
    }

    [Fact]
    public void LocalizedLabelFollowsTheLanguage()
    {
        var key = new TextKey("test.label", "Save");
        Localization.Register(new StringTable("ru").Add(key, "Сохранить"));

        WithCulture("en-US", () =>
        {
            var label = new Label().Localize(key);
            var form = new Form { Size = new Size(200, 100), Content = label };
            new HeadlessPlatform().CreateWindow(form);

            Assert.Equal("Save", label.Text);

            Localization.Culture = new CultureInfo("ru-RU");
            Assert.Equal("Сохранить", label.Text);
        });
    }

    [Fact]
    public void DetachedElementCatchesUpOnAttach()
    {
        var key = new TextKey("test.detached", "Open");
        Localization.Register(new StringTable("ru").Add(key, "Открыть"));

        WithCulture("en-US", () =>
        {
            var button = new Button().Localize(key);
            var panel = new StackPanel();
            panel.Children.Add(button);

            var form = new Form { Size = new Size(200, 100), Content = panel };
            new HeadlessPlatform().CreateWindow(form);

            panel.Children.Remove(button);

            // switched while the button is out of every tree: no form walks it
            Localization.Culture = new CultureInfo("ru-RU");
            Assert.Equal("Open", button.Text);

            panel.Children.Add(button);
            Assert.Equal("Открыть", button.Text);
        });
    }

    [Fact]
    public void RightToLeftCultureMirrorsLayoutUnlessTurnedOff()
    {
        WithCulture("ar-SA", () =>
        {
            var label = new Label { Text = "x" };
            var form = new Form { Size = new Size(200, 100), Content = label };
            new HeadlessPlatform().CreateWindow(form);

            Assert.Equal(FlowDirection.RightToLeft, label.EffectiveFlowDirection);

            Localization.MirrorLayoutForRightToLeft = false;

            try
            {
                Assert.Equal(FlowDirection.LeftToRight, label.EffectiveFlowDirection);
            }
            finally
            {
                Localization.MirrorLayoutForRightToLeft = true;
            }
        });
    }
}