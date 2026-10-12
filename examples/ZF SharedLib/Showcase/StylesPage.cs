using System.Runtime.CompilerServices;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Forms.Validation;

namespace ZF_SharedLib.Showcase;

/// <summary>Styling: a style sheet with classes and pseudo-classes, the theme's color
/// roles, a sheet to edit live and an inspector of a live control.</summary>
internal static class StylesPage
{
    /// <summary>The roles of a theme's colors a sheet names with <c>@</c>.</summary>
    private static readonly string[] Roles =
    [
        "Background", "Surface", "SurfaceHover", "Border", "Text", "TextSecondary",
        "Accent", "AccentHover", "Selection", "Success", "Warning", "Error",
    ];

    /// <summary>What the editor starts with.</summary>
    internal const string PlaygroundSheet = """
        /* edit and press Apply: these styles reach only the sample below */
        Button {
            CornerRadius: 20;
            Padding: 20 8;
        }

        Button:hover { BackgroundColor: alpha(@Accent, 0.25); }

        Label.title {
            TextStyle: Subtitle;
            TextColor: @Accent;
        }

        CheckBox:checked { TextColor: @Success; }
        """;

    public static UIElement Create()
    {
        StackPanel page = Demo.Page("Styles", "Style sheets: selectors, classes and states, theme colors, transitions.",
            Demo.Section("A style sheet", SheetDemo(), "In Debug, Assets/styles.zss is watched: edit and save it, and the page restyles at once"),
            Demo.Section("Theme colors", Swatches(), "A sheet names the theme's roles with @: they follow the theme chosen in Settings"),
            Demo.Section("Live editor", Playground(), "StyleSheet.Parse reports every mistake with its line, and keeps the rules that are right"),
            Demo.Section("Inspector", Inspector(), "Classes is editable: type \"accent\" or \"danger\"; each value says where it came from"));

        // the sheet's rules start with .styles-demo: they reach this page only
        page.Classes.Add("styles-demo");
        LoadSheet(page);

        // the swatches: one rule per role
        page.Styles.Add(StyleSheet.Parse(
            string.Concat(Roles.Select(role => $"Border.role-{role} {{ Background: @{role}; }}\n")),
            "roles.zss"));

        return page;
    }

    private static UIElement SheetDemo()
    {
        var accent = new Button { Text = "Accent" };
        accent.Classes.Add("accent");

        var danger = new Button { Text = "Danger" };
        danger.Classes.Add("danger");

        var toggle = new Button { Text = "Toggle the class of the first" };
        toggle.Click += (_, _) => accent.Classes.Toggle("accent");

        // a zebra list: :odd tints every other row, :hover the one under the pointer
        var zebra = new StackPanel { Orientation = Orientation.Vertical, Size = new(280, float.NaN) };
        zebra.Classes.Add("zebra");

        for (int i = 1; i <= 6; i++)
            zebra.Children.Add(new Label { Text = $"Row {i}" });

        // a group: :focus-within lights it up while the focus is inside
        var group = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            Size = new(320, float.NaN),
            Children =
            {
                new Label { Text = "E-mail: the group lights up while you type" },
                new TextBox { Validator = Validators.Email(), Watermark = "name@example.com" },
                new CheckBox { Text = "Remember me" },
            },
        };
        group.Classes.Add("group");

        return Demo.Column(Demo.Row(accent, danger, toggle), Demo.Row(zebra, group));
    }

    private static UIElement Swatches()
    {
        var row = Demo.Row();

        foreach (string role in Roles)
        {
            var swatch = new Border { Size = new(96, 48), CornerRadius = new(6), BorderWidth = 1 };
            swatch.Classes.Add($"role-{role}");
            swatch.Classes.Add("swatch");

            row.Children.Add(Demo.Column(swatch, new Label { Text = $"@{role}", TextStyle = TextStyle.Caption }));
        }

        return row;
    }

    private static UIElement Playground()
    {
        // the sample has styles of its own: they beat the form's and the application's
        var sample = Demo.Column(
            new Label { Text = "A title", Classes = { "title" } },
            Demo.Row(new Button { Text = "Button" }, new PrimaryButton { Text = "Primary" }),
            new CheckBox { Text = "Check me", IsChecked = true });

        var editor = new TextBox
        {
            IsMultiline = true,
            Text = PlaygroundSheet,
            Font = ZeppelinForms.Drawing.Font.Monospace,
            Size = new(float.NaN, 240),
        };

        Label result = Demo.Note("Press Apply");

        void Apply()
        {
            StyleSheet sheet = StyleSheet.Parse(editor.Text ?? string.Empty, "editor.zss");
            sample.Styles.Replace(sheet.Styles);

            result.Text = sheet.Diagnostics.Count == 0
                ? $"Applied: {sheet.Styles.Count} rule(s)"
                : string.Join("\n", sheet.Diagnostics.Take(3).Select(d => d.ToString()));
        }

        var apply = new PrimaryButton { Text = "Apply" };
        apply.Click += (_, _) => Apply();

        var reset = new Button { Text = "Reset" };
        reset.Click += (_, _) => { editor.Text = PlaygroundSheet; Apply(); };

        Apply();

        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, CrossAxisAlignment = CrossAxisAlignment.Stretch };
        root.Children.AddRange([editor, Demo.Row(apply, reset), result, sample]);
        return root;
    }

    private static UIElement Inspector()
    {
        var target = new Button { Text = "Inspect me" };

        var grid = new PropertyGrid
        {
            SelectedObject = target,
            Size = new(float.NaN, 320),
            OverflowY = Overflow.Auto,
        };

        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, CrossAxisAlignment = CrossAxisAlignment.Stretch };
        root.Children.AddRange([Demo.Row(target), grid]);
        return root;
    }

    /// <summary>In Debug the sheet is read from the project folder and watched: saving
    /// it in the editor restyles the running showcase. Elsewhere — a release build,
    /// a phone, a browser — it is the copy among the assets.</summary>
    private static void LoadSheet(UIElement page)
    {
#if DEBUG
        string source = Path.Combine(Path.GetDirectoryName(ThisFile())!, "..", "Assets", "styles.zss");

        if (File.Exists(source))
        {
            page.Styles.Load(Path.GetFullPath(source), watch: true);
            return;
        }
#endif

        page.Styles.Add(StyleSheet.LoadAsset("styles.zss"));
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
