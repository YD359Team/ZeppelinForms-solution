using System.Globalization;
using System.Runtime.InteropServices;
using ZeppelinForms;
using ZeppelinForms.Animation;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Theming;

namespace ZF_SharedLib.Showcase;

/// <summary>The application's settings: theme and accent, language and direction,
/// text size, motion. Every page follows them at once.</summary>
internal static class SettingsPage
{
    private static readonly (string Name, Theme Theme)[] ThemeChoices =
    [
        ("Light", Themes.Light),
        ("Dark", Themes.Dark),
        ("Fluent light", Themes.FluentLight),
        ("Fluent dark", Themes.FluentDark),
        ("High contrast black", Themes.HighContrastBlack),
        ("High contrast white", Themes.HighContrastWhite),
    ];

    private static readonly (string Name, Color? Color)[] Accents =
    [
        ("The theme's", null),
        ("Blue", new Color(255, 0x0D, 0x6E, 0xFD)),
        ("Purple", new Color(255, 0x66, 0x10, 0xF2)),
        ("Pink", new Color(255, 0xD6, 0x33, 0x84)),
        ("Orange", new Color(255, 0xFD, 0x7E, 0x14)),
        ("Green", new Color(255, 0x19, 0x87, 0x54)),
    ];

    private static readonly (string Name, string Culture)[] Languages =
    [
        ("English", "en-US"),
        ("Русский", "ru-RU"),
    ];

    public static UIElement Create(Form form) =>
        Demo.Page("Settings", "Application-wide: every open form and every page follows them at once.",
            Demo.Section("Appearance", Appearance(), "App.UseSystemTheme follows the system's light and dark; App.Theme is a choice of one's own"),
            Demo.Section("Language and direction", Language(form), "Localization.Culture switches the framework's own texts and the formats of dates and numbers"),
            Demo.Section("Accessibility", Accessibility()),
            Demo.Section("About", About()));

    private static UIElement Appearance()
    {
        var theme = new ComboBox { SelectedIndex = -1 };
        theme.Items.Add("Follow the system");
        theme.Items.AddRange(ThemeChoices.Select(t => t.Name));

        var accent = new ComboBox { SelectedIndex = -1 };
        accent.Items.AddRange(Accents.Select(a => a.Name));

        // what is in effect now, so that the page opens showing it
        theme.SelectedIndex = App.IsFollowingSystemTheme ? 0 : 1 + Math.Max(0, Array.FindIndex(ThemeChoices, t => t.Theme.Name == App.RequestedTheme.Name));
        accent.SelectedIndex = 0;

        void Apply()
        {
            Color? color = Accents[Math.Max(0, accent.SelectedIndex)].Color;

            if (theme.SelectedIndex <= 0)
            {
                // the pair to follow; an accent of one's own goes on both
                Theme light = Themes.FluentLight, dark = Themes.FluentDark;

                if (color is { } c)
                    App.UseSystemTheme(light.WithAccent(c), dark.WithAccent(c), followAccent: false);
                else
                    App.UseSystemTheme(light, dark);

                return;
            }

            Theme chosen = ThemeChoices[theme.SelectedIndex - 1].Theme;
            App.Theme = color is { } accentColor ? chosen.WithAccent(accentColor) : chosen;
        }

        theme.SelectionChanged += (_, _) => Apply();
        accent.SelectionChanged += (_, _) => Apply();

        return Demo.Row(Demo.Labeled("Theme", theme), Demo.Labeled("Accent", accent));
    }

    private static UIElement Language(Form form)
    {
        var language = new ComboBox { SelectedIndex = -1 };
        language.Items.AddRange(Languages.Select(l => l.Name));
        language.SelectedIndex = Math.Max(0, Array.FindIndex(Languages, l => l.Culture == Localization.Culture.Name));

        Label sample = Demo.Note(Sample());

        language.SelectionChanged += (_, _) =>
        {
            if (language.SelectedIndex < 0) return;

            var culture = CultureInfo.GetCultureInfo(Languages[language.SelectedIndex].Culture);

            // the framework's texts — dialogs, the context menu of a text box —
            // and the application's own formatting
            Localization.Culture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            sample.Text = Sample();
        };

        var rtl = new ToggleSwitch { Text = "Right to left", IsOn = form.FlowDirection == FlowDirection.RightToLeft };
        rtl.Toggled += (_, _) => form.FlowDirection = rtl.IsOn ? FlowDirection.RightToLeft : null;

        return Demo.Column(Demo.Row(Demo.Labeled("Language", language), rtl), sample);

        static string Sample() =>
            $"{DateTime.Now:D} · {1234567.89.ToString("N2", CultureInfo.CurrentCulture)}";
    }

    private static UIElement Accessibility()
    {
        var textScale = new ToggleSwitch { Text = "Follow the system's text size", IsOn = App.IsFollowingSystemTextScale };
        textScale.Toggled += (_, _) => App.UseSystemTextScale(textScale.IsOn);

        var motion = new ComboBox { Items = { "As the system says", "Always animate", "Reduce motion" } };
        motion.SelectedIndex = (int)Motion.Preference;
        motion.SelectionChanged += (_, _) => Motion.Preference = (MotionPreference)motion.SelectedIndex;

        return Demo.Column(textScale, Demo.Labeled("Motion", motion));
    }

    private static UIElement About() => Demo.Column(
        new Label { Text = $"ZeppelinForms {HomePage.FrameworkVersion}", TextStyle = TextStyle.BodyStrong },
        Demo.Note($"{RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription}"),
        new LinkLabel { Text = "Source code and issues", Url = "https://github.com/YD359Team/ZeppelinForms-solution" });
}
