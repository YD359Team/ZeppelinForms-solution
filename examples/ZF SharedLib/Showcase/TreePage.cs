using ZeppelinForms.Animation;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Controls.Tree;
using ZF_SharedLib.Models;

namespace ZF_SharedLib.Showcase;

/// <summary>A tree: games under their developers, expanded and collapsed by hand,
/// by the keys or all at once.</summary>
internal static class TreePage
{
    public static UIElement Create()
    {
        Label selection = Demo.Note("Select a node");

        var tree = new TreeView
        {
            // a fixed height, so that the tree scrolls inside itself
            // rather than stretching the page to its full depth
            Size = new(float.NaN, 320),
            ScrollBarMode = ScrollBarMode.Inline,

            // without it Content would print through the record's ToString —
            // with all of its fields at once
            ItemText = node => node.Content switch
            {
                Game game => $"{game.Title} ({game.Year})",
                string developer => developer,
                _ => string.Empty,
            },

            // an icon by the kind of node, a check box for "played", several nodes at
            // once with Ctrl and Shift, F2 to rename
            ItemIcon = node => node.Content is Game ? Icons.Image : Icons.Box,
            CheckBoxes = true,
            SelectionMode = ZeppelinForms.Forms.Enums.SelectionMode.Extended,
            LabelEdit = true,
        };

        // a game is renamed by its title, without the year the caption adds
        tree.NodeEditing += (_, e) =>
        {
            if (e.Node.Content is Game game) e.Text = game.Title;
        };

        // a developer's content is the string itself and takes the text by itself;
        // a game is a record, and the handler makes the renamed one
        tree.NodeEdited += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Text)) e.Cancel = true;
            else if (e.Node.Content is Game game) e.Node.Content = game with { Title = e.Text.Trim() };
        };

        tree.NodeInvoked += (_, e) =>
        {
            if (e.Node.Content is not Game game) return;

            selection.Text = $"Opened {game.Title}";
            e.Handled = true;
        };

        tree.NodeContextMenu = node =>
        {
            var rename = new MenuItem { Text = "&Rename", InputGestureText = "F2" };
            rename.Click += (_, _) => tree.BeginEdit(node);

            var played = new MenuItem { Text = "Mark as &played" };
            played.Click += (_, _) =>
            {
                foreach (TreeNode selected in tree.SelectedNodes) selected.IsChecked = true;
            };

            return [rename, played];
        };

        foreach (IGrouping<string, Game> group in Samples.Games
            .GroupBy(game => game.Developer)
            .OrderBy(group => group.Key, StringComparer.CurrentCulture))
        {
            TreeNode developer = new(group.Key);

            foreach (Game game in group.OrderBy(game => game.Year))
                developer.Children.Add(new TreeNode(game));

            tree.Nodes.Add(developer);
        }

        // the first node is expanded and the rest are not: both states
        // of the expander are on the screen at once
        if (tree.Nodes.Count > 0) tree.Nodes[0].IsExpanded = true;

        tree.SelectionChanged += (_, node) =>
            selection.Text = tree.SelectedNodes.Count > 1
                ? $"{tree.SelectedNodes.Count} nodes selected"
                : node?.Content switch
                {
                    Game game => $"{game.Title} — {game.Developer}, {game.Year}",
                    string developer => $"{developer}: {Samples.Games.Count(game => game.Developer == developer)} game(s)",
                    _ => "Select a node",
                };

        // the rows below an expanded node slide down instead of jumping
        tree.Children.OfType<VirtualizingStackPanel>().Single()
            .ChildrenLayoutTransition = LayoutTransition.Speed();

        var expand = new Button { Text = "Expand all" };
        expand.Click += (_, _) => { foreach (TreeNode node in tree.Nodes) node.Expand(); };

        var collapse = new Button { Text = "Collapse all" };
        collapse.Click += (_, _) => { foreach (TreeNode node in tree.Nodes) node.Collapse(); };

        var reveal = new Button { Text = "Find Hades" };
        reveal.Click += (_, _) =>
        {
            TreeNode? hades = tree.Nodes.SelectMany(n => n.Children).FirstOrDefault(n => n.Content is Game { Title: "Hades" });
            if (hades is null) return;

            // the path to it opens, and it is selected
            hades.ExpandAncestors();
            tree.SelectedNode = hades;
        };

        var demo = new StackPanel
        {
            Orientation = ZeppelinForms.Forms.Enums.Orientation.Vertical,
            Spacing = 10,
            CrossAxisAlignment = ZeppelinForms.Forms.Enums.CrossAxisAlignment.Stretch,
        };
        demo.Children.AddRange([Demo.Row(expand, collapse, reveal), tree, selection]);

        return Demo.Page("Tree", "Nested data: nodes with children, expanded on demand.",
            Demo.Section("Tree view", demo, "Right and Left expand and collapse, Up and Down move, Shift and Ctrl select several; Space checks the selected nodes, F2 renames, Enter or a double click opens a game, the right button has a menu; only the visible rows are made"));
    }
}
