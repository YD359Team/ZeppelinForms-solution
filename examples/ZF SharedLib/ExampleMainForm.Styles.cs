using System.Runtime.CompilerServices;
using ZeppelinForms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Forms.Validation;

namespace ZF_SharedLib;

/// <summary>The "Styles" page: classes, pseudo-classes and a style sheet.</summary>
public partial class ExampleMainForm
{
    private StyleSheetLink? _stylesLink;

    private StackPanel GetStylesView()
    {
        // the sheet is loaded once, for the whole form; the page's selectors start
        // with .styles-demo, so it touches nothing else
        _stylesLink ??= LoadExampleSheet();

        var zebra = new StackPanel { Orientation = Orientation.Vertical };
        zebra.Classes.Add("zebra");

        for (int i = 1; i <= 6; i++)
            zebra.Children.Add(new Label { Text = $"Row {i}" });

        var email = new TextBox { Validator = Validators.Email() };

        var group = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            Children =
            {
                new Label { Text = "E-mail — the group lights up while you type" },
                email,
                new CheckBox { Text = "Remember me" },
            },
        };
        group.Classes.Add("group");

        var accent = new Button { Text = "Accent" };
        accent.Classes.Add("accent");

        var danger = new Button { Text = "Danger" };
        danger.Classes.Add("danger");

        var toggle = new Button { Text = "Toggle the class of the first button" };
        toggle.Click += (_, _) => accent.Classes.Toggle("accent");

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Padding = new(16),
            Spacing = 12,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { accent, danger, toggle },
                },
                zebra,
                group,
            },
        };

        root.Classes.Add("styles-demo");
        return root;
    }

    /// <summary>In Debug the sheet is read from the project folder and followed:
    /// saving it in the editor restyles the running example. Elsewhere it is the
    /// copy among the assets.</summary>
    private StyleSheetLink LoadExampleSheet()
    {
#if DEBUG
        string source = Path.Combine(Path.GetDirectoryName(ThisFile())!, "Assets", "styles.zss");

        if (File.Exists(source))
            return Styles.Load(source, watch: true);
#endif

        return Styles.Add(StyleSheet.LoadAsset("styles.zss"));
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}