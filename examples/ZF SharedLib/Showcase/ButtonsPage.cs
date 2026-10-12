using ZeppelinForms.Drawing;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>Buttons of every kind: the plain one and its accents, icons, toggles,
/// the split button, links and the file button.</summary>
internal static class ButtonsPage
{
    public static UIElement Create()
    {
        Label clicks = Demo.Note("Nothing clicked yet");
        int count = 0;

        Button Counted(Button button)
        {
            button.Click += (_, _) => clicks.Text = $"{button.Text}: clicked {++count} time(s)";
            return button;
        }

        var kinds = Demo.Column(
            Demo.Row(
                Counted(new Button { Text = "Button" }),
                Counted(new PrimaryButton { Text = "Primary" }),
                Counted(new SecondaryButton { Text = "Secondary" }),
                Counted(new DangerButton { Text = "Danger" }),
                new Button { Text = "Disabled", IsEnabled = false }),
            clicks);

        var icons = Demo.Row(
            new Button { Text = "Start", Icon = Icons.Heart, IconPlacement = IconPlacement.Start },
            new Button { Text = "End", Icon = Icons.Send, IconPlacement = IconPlacement.End },
            new Button { Text = "Top", Icon = Icons.Image, IconPlacement = IconPlacement.Top, Padding = new(12, 8) },
            new Button { Text = "Bottom", Icon = Icons.Chart, IconPlacement = IconPlacement.Bottom, Padding = new(12, 8) },
            new Button { Icon = Icons.Trash, ToolTip = "An icon alone: the tooltip says what it does" });

        Label alignment = Demo.Note("Alignment: left");
        var left = new ToggleButton { Text = "Left", GroupName = "align", IsChecked = true };
        var center = new ToggleButton { Text = "Center", GroupName = "align" };
        var right = new ToggleButton { Text = "Right", GroupName = "align" };
        var bold = new ToggleButton { Text = "Bold" };

        foreach (ToggleButton toggle in new[] { left, center, right })
            toggle.CheckedChanged += (_, _) => { if (toggle.IsChecked) alignment.Text = $"Alignment: {toggle.Text!.ToLowerInvariant()}"; };

        bold.CheckedChanged += (_, _) => alignment.TextStyle = bold.IsChecked ? TextStyle.BodyStrong : TextStyle.None;

        var toggles = Demo.Column(Demo.Row(left, center, right, bold), alignment);

        Label saved = Demo.Note("The main part repeats the last choice");
        var save = new SplitButton
        {
            Text = "Save",
            RepeatLastAction = true,
            Items = { new() { Text = "Save" }, new() { Text = "Save as…" }, new() { Text = "Save all" } },
        };
        save.SelectionChanged += (_, _) => saved.Text = $"Chosen: {save.SelectedItem?.Text}";

        var split = Demo.Column(Demo.Row(save), saved);

        var link = new LinkLabel { Text = "ZeppelinForms on GitHub", Url = "https://github.com/YD359Team/ZeppelinForms-solution" };
        var linkHint = Demo.Note("A link turns to the visited color once followed");
        link.Navigate += (_, _) => linkHint.Text = "Followed";

        Label files = Demo.Note("No files chosen");
        var attach = new AttachButton { AllowMultiple = true };
        attach.FilesChanged += (_, _) => files.Text = "The files changed";

        return Demo.Page("Buttons", "Buttons in every role, with icons, as toggles and with a menu.",
            Demo.Section("Kinds", kinds, "Primary is the main action of a screen, Danger the destructive one"),
            Demo.Section("Icons", icons, "Button.Icon takes SVG path data or a picture; IconPlacement puts it around the text"),
            Demo.Section("Toggle buttons", toggles, "Buttons with one GroupName behave as radio buttons; without one, as a check box"),
            Demo.Section("Split button", split),
            Demo.Section("Link", Demo.Column(link, linkHint)),
            Demo.Section("Files", Demo.Column(attach, files), "AttachButton opens the platform's file dialog"));
    }
}
