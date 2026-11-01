using ZeppelinForms.Core.Collections;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Docking;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Controls.Tree;

namespace ZF_SharedLib.Showcase;

/// <summary>An IDE's layout in a card: panels docked, tabbed, floated and pinned to the
/// edges, by the user's hands, and the arrangement saved and put back.</summary>
internal static class DockingPage
{
    public static UIElement Create() =>
        Demo.Page("Docking", "Panels the user arranges, as in an IDE: dragged by their tabs, split, floated, pinned to an edge.",
            Demo.Section("Dock host", Ide(),
                "Drag a tab off its strip and drop it on the compass to dock it, anywhere else to float it; Esc cancels. A double click floats a tab and docks a window back; the pin slides a panel to its edge"));

    private static UIElement Ide()
    {
        Label status = Demo.Note("The layout is as it was made");

        var host = new DockHost { Size = new(float.NaN, 460) };

        var output = new TextBox { IsMultiline = true, IsReadOnly = true };

        void Log(string line) => output.Text = $"{line}\n{output.Text}";

        DockItem editor = new("editor", "Program.cs", new TextBox
        {
            IsMultiline = true,
            Text = "var host = new DockHost();\nhost.DockTo(new DockItem(\"files\", \"Explorer\", tree), DockPosition.Left);\n",
        })
        {
            Icon = Icons.Text,
        };

        DockItem readme = new("readme", "README.md", new TextBox
        {
            IsMultiline = true,
            Text = "# Docking\n\nPanels docked, tabbed, floating and auto-hidden.\n",
        });

        DockItem explorer = new("files", "Explorer", new TreeView
        {
            Nodes =
            {
                new TreeNode("src") { Children = { new TreeNode("Program.cs"), new TreeNode("App.cs") }, IsExpanded = true },
                new TreeNode("README.md"),
            },
        })
        {
            Icon = Icons.Tree,
            CanClose = false,
        };

        // a tool window: closed, it is hidden, and "Show all" brings it back
        DockItem log = new("output", "Output", output) { HideOnClose = true };

        DockItem properties = new("props", "Properties", Demo.Column(
            Demo.Labeled("Name", new TextBox { Text = "host" }),
            Demo.Labeled("Theme", new ComboBox { Items = { "Light", "Dark" }, SelectedIndex = 0 })))
        {
            HideOnClose = true,
        };

        host.DockTo(editor);
        host.DockTo(readme, DockPosition.Tabbed, editor);
        host.DockTo(explorer, DockPosition.Left, size: 0.24f);
        host.DockTo(log, DockPosition.Bottom, editor, 0.3f);
        host.DockTo(properties, DockPosition.Right, size: 0.24f);
        host.AutoHide(properties);
        host.Show(editor);

        string initial = host.SaveLayout();
        string? saved = null;

        host.ActiveItemChanged += (_, _) => Log($"Active: {host.ActiveItem?.Title ?? "none"}");
        host.ItemClosed += (_, item) => Log($"Closed: {item.Title}");
        host.LayoutChanged += (_, _) => status.Text = "The layout changed";

        // a document asks before it closes
        host.ItemClosing += (_, e) =>
        {
            if (e.Item == editor) Log("Program.cs has changes: it stays");

            e.Cancel = e.Item == editor;
        };

        var save = new Button { Text = "Save layout" };
        save.Click += (_, _) =>
        {
            saved = host.SaveLayout();
            status.Text = $"Saved: {saved.Length} characters of JSON";
        };

        var restore = new Button { Text = "Restore saved" };
        restore.Click += (_, _) =>
        {
            if (saved is null) return;

            host.LoadLayout(saved);
            status.Text = "Restored";
        };

        var reset = new Button { Text = "Reset" };
        reset.Click += (_, _) =>
        {
            host.LoadLayout(initial);
            status.Text = "As it was made";
        };

        var showAll = new Button { Text = "Show all" };
        showAll.Click += (_, _) =>
        {
            foreach (DockItem item in host.Items)
                if (item.State == DockState.Hidden) host.Show(item);
        };

        var root = new StackPanel
        {
            Orientation = ZeppelinForms.Forms.Enums.Orientation.Vertical,
            Spacing = 10,
            CrossAxisAlignment = ZeppelinForms.Forms.Enums.CrossAxisAlignment.Stretch,
        };

        root.Children.AddRange([Demo.Row(save, restore, reset, showAll, status), host]);
        return root;
    }
}
