using Xunit;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Controls.Tree;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.UnitTests;

/// <summary>0.16: the tree of a model editor — several nodes selected, check boxes,
/// icons, invoking a node, renaming it in place, the node under the cursor.</summary>
[Collection("Platform")]
public class TreeViewTests
{
    /// <summary>Bones under a model: "Root" with "Spine" and "Hips", "Spine" with "Head".</summary>
    private static (Form Form, TreeView Tree, TreeNode Root, TreeNode Spine, TreeNode Head, TreeNode Hips) Create(
        Action<TreeView>? setup = null)
    {
        var tree = new TreeView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        setup?.Invoke(tree);

        var head = new TreeNode("Head");
        var spine = new TreeNode("Spine") { Children = { head } };
        var hips = new TreeNode("Hips");
        var root = new TreeNode("Root") { Children = { spine, hips } };

        tree.Nodes.Add(root);
        tree.ExpandAll();

        var form = new Form { Size = new Size(400, 300), Content = tree };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, tree, root, spine, head, hips);
    }

    private static TreeViewItem Row(TreeView tree, TreeNode node) =>
        tree.Children.OfType<VirtualizingStackPanel>().Single()
            .Children.OfType<TreeViewItem>().Single(row => ReferenceEquals(row.Node, node));

    /// <summary>A point on the node's caption, in the form's coordinates.</summary>
    private static Point Caption(TreeView tree, TreeNode node)
    {
        TreeViewItem row = Row(tree, node);
        Point at = row.GetAbsolutePosition();
        Rectangle caption = row.CaptionBounds;

        return new Point(at.X + caption.X + 4, at.Y + row.ActualSize.Height / 2f);
    }

    private static void Click(Form form, Point at, KeyModifiers modifiers = KeyModifiers.None)
    {
        form.OnPointerMove(at, modifiers);
        form.OnPointerDown(at, MouseButton.Left, modifiers);
        form.OnPointerUp(at, MouseButton.Left, modifiers);
    }

    private static void RightPress(Form form, Point at)
    {
        form.OnPointerDown(at, MouseButton.Right);
        form.OnPointerUp(at, MouseButton.Right);
    }

    // ===== selection =====

    [Fact]
    public void ShiftSelectsTheRowsBetween()
    {
        var (form, tree, root, spine, head, hips) = Create(t => t.SelectionMode = SelectionMode.Extended);

        HeadlessInput.Click(form, Caption(tree, spine).X, Caption(tree, spine).Y);
        Click(form, Caption(tree, hips), KeyModifiers.Shift);

        Assert.Equal([spine, head, hips], tree.SelectedNodes);
        Assert.Same(hips, tree.SelectedNode);
        Assert.False(tree.IsSelected(root));
    }

    [Fact]
    public void CtrlAddsAndRemovesOneNode()
    {
        var (form, tree, root, _, head, _) = Create(t => t.SelectionMode = SelectionMode.Extended);

        HeadlessInput.Click(form, Caption(tree, root).X, Caption(tree, root).Y);
        Click(form, Caption(tree, head), KeyModifiers.Control);
        Assert.Equal([root, head], tree.SelectedNodes);

        Click(form, Caption(tree, root), KeyModifiers.Control);
        Assert.Equal([head], tree.SelectedNodes);
    }

    [Fact]
    public void ShiftArrowsExtendFromTheAnchor()
    {
        var (form, tree, root, spine, head, _) = Create(t => t.SelectionMode = SelectionMode.Extended);

        tree.SelectedNode = root;
        tree.Focus();

        HeadlessInput.PressKey(form, Key.Down, KeyModifiers.Shift);
        HeadlessInput.PressKey(form, Key.Down, KeyModifiers.Shift);

        Assert.Equal([root, spine, head], tree.SelectedNodes);

        HeadlessInput.PressKey(form, Key.Down);
        Assert.Single(tree.SelectedNodes);
    }

    [Fact]
    public void SingleModeKeepsOneNode()
    {
        var (form, tree, root, _, head, _) = Create();

        HeadlessInput.Click(form, Caption(tree, root).X, Caption(tree, root).Y);
        Click(form, Caption(tree, head), KeyModifiers.Control);

        Assert.Equal([head], tree.SelectedNodes);
    }

    [Fact]
    public void CollapsingDeselectsTheHiddenNodes()
    {
        var (_, tree, _, spine, head, hips) = Create(t => t.SelectionMode = SelectionMode.Multiple);

        tree.SetSelected(head, true);
        tree.SetSelected(hips, true);

        spine.Collapse();

        Assert.Equal([hips], tree.SelectedNodes);
        Assert.Same(hips, tree.SelectedNode);
    }

    [Fact]
    public void ARightPressSelectsTheNodeUnderTheCursor()
    {
        var (form, tree, root, spine, head, hips) = Create(t => t.SelectionMode = SelectionMode.Extended);

        tree.SelectedNode = root;

        Point at = Caption(tree, hips);
        RightPress(form, at);
        Assert.Equal([hips], tree.SelectedNodes);

        // a node inside a multiple selection keeps it: the menu acts on all of them
        tree.SetSelected(spine, true);
        RightPress(form, Caption(tree, spine));
        Assert.Equal([spine, hips], tree.SelectedNodes);

        Assert.False(tree.IsSelected(head));
    }

    [Fact]
    public void TheNodeMenuIsBuiltForTheNodeUnderTheCursor()
    {
        TreeNode? asked = null;
        var (form, tree, _, _, head, _) = Create(t => t.NodeContextMenu = node =>
        {
            asked = node;
            return [new MenuItem { Text = "Hide" }];
        });

        Point at = Caption(tree, head);
        RightPress(form, at);
        form.OnContextMenu(at);

        Assert.Same(head, asked);
        Assert.Same(head, tree.SelectedNode);
    }

    [Fact]
    public void NodeAtFindsTheRowUnderAPoint()
    {
        var (_, tree, _, spine, _, _) = Create();

        Assert.Same(spine, tree.NodeAt(Caption(tree, spine)));
        Assert.Null(tree.NodeAt(new Point(-10, -10)));
    }

    // ===== check boxes =====

    [Fact]
    public void ACheckBoxClickChecksWithoutSelecting()
    {
        var (form, tree, _, spine, _, _) = Create(t => t.CheckBoxes = true);

        TreeNode? changed = null;
        tree.NodeCheckedChanged += (_, node) => changed = node;

        TreeViewItem row = Row(tree, spine);
        Point at = row.GetAbsolutePosition();
        float boxX = at.X + row.Padding.Left + spine.Level * tree.Indent - tree.Indent + TreeViewItem.GlyphWidth + 6;

        HeadlessInput.Click(form, boxX, at.Y + row.ActualSize.Height / 2f);

        Assert.True(spine.IsChecked);
        Assert.Same(spine, changed);
        Assert.False(tree.IsSelected(spine));
    }

    [Fact]
    public void SpaceChecksAllTheSelectedNodesTogether()
    {
        var (form, tree, _, spine, head, hips) = Create(t =>
        {
            t.CheckBoxes = true;
            t.SelectionMode = SelectionMode.Multiple;
        });

        tree.SetSelected(head, true);
        tree.SetSelected(hips, true);
        hips.IsChecked = true;
        tree.Focus();

        // the lead is Hips, checked: all of them get unchecked, as it does
        HeadlessInput.PressKey(form, Key.Space);

        Assert.False(hips.IsChecked);
        Assert.False(head.IsChecked);

        HeadlessInput.PressKey(form, Key.Space);

        Assert.True(hips.IsChecked);
        Assert.True(head.IsChecked);
        Assert.False(spine.IsChecked);
    }

    [Fact]
    public void CheckingAParentLeavesTheChildren()
    {
        var (_, _, root, spine, _, _) = Create(t => t.CheckBoxes = true);

        root.IsChecked = true;

        Assert.False(spine.IsChecked);
    }

    [Fact]
    public void TheNodeSaysItIsChecked()
    {
        var (_, tree, root, _, _, _) = Create(t => t.CheckBoxes = true);

        AccessibilityPeer peer = tree.GetAccessibilityPeer()!.Children[0];

        Assert.True(peer.Toggle());
        Assert.True(root.IsChecked);
        Assert.True(peer.States.HasFlag(AccessibilityStates.Checked));
    }

    // ===== icons =====

    [Fact]
    public void AnIconMakesRoomForItselfInEveryRow()
    {
        var (_, tree, root, _, head, _) = Create();

        float before = Row(tree, root).CaptionBounds.X;

        head.Icon = IconSource.FromPath("M0 0 L10 10");

        Assert.True(tree.ShowsIcons);
        Assert.Equal(before + TreeViewItem.IconWidth, Row(tree, root).CaptionBounds.X, 0.01f);
    }

    [Fact]
    public void TheTreeGivesIconsByKind()
    {
        var (_, tree, root, _, _, _) = Create(t => t.ItemIcon = node =>
            node.HasChildren ? IconSource.FromPath("M0 0 L1 1") : null);

        Assert.True(tree.ShowsIcons);
        Assert.NotNull(tree.GetItemIcon(root));
    }

    // ===== invoking =====

    [Fact]
    public void EnterInvokesTheNode()
    {
        var (form, tree, _, _, head, _) = Create();

        TreeNode? invoked = null;
        tree.NodeInvoked += (_, e) => { invoked = e.Node; e.Handled = true; };

        tree.SelectedNode = head;
        tree.Focus();
        HeadlessInput.PressKey(form, Key.Enter);

        Assert.Same(head, invoked);
    }

    [Fact]
    public void AHandledInvokeDoesntToggleTheNode()
    {
        var (form, tree, _, spine, _, _) = Create();

        tree.NodeInvoked += (_, e) => e.Handled = true;

        tree.SelectedNode = spine;
        tree.Focus();
        HeadlessInput.PressKey(form, Key.Enter);

        Assert.True(spine.IsExpanded);
    }

    [Fact]
    public void ADoubleClickInvokesTheNode()
    {
        var (form, tree, _, _, head, _) = Create();

        TreeNode? invoked = null;
        tree.NodeInvoked += (_, e) => invoked = e.Node;

        Point at = Caption(tree, head);
        HeadlessInput.DoubleClick(form, at.X, at.Y);

        Assert.Same(head, invoked);
    }

    // ===== renaming =====

    [Fact]
    public void EnterCommitsTheNewName()
    {
        var (form, tree, _, _, head, _) = Create();

        Assert.True(tree.BeginEdit(head));
        Assert.Same(head, tree.EditingNode);

        TextBox editor = tree.Children.OfType<TextBox>().Single();
        Assert.True(editor.IsFocused);

        editor.SelectAll();
        HeadlessInput.TypeText(form, "Skull");
        HeadlessInput.PressKey(form, Key.Enter);

        Assert.Equal("Skull", head.Content);
        Assert.Null(tree.EditingNode);
        Assert.Empty(tree.Children.OfType<TextBox>());
        Assert.True(tree.IsFocused);
    }

    [Fact]
    public void EscapeKeepsTheOldName()
    {
        var (form, tree, _, _, head, _) = Create();

        bool edited = false;
        tree.NodeEdited += (_, _) => edited = true;

        tree.BeginEdit(head);
        HeadlessInput.TypeText(form, "Skull");
        HeadlessInput.PressKey(form, Key.Escape);

        Assert.Equal("Head", head.Content);
        Assert.False(edited);
    }

    [Fact]
    public void AHandlerCanRefuseTheEditOrTheName()
    {
        var (form, tree, _, spine, head, _) = Create();

        tree.NodeEditing += (_, e) => e.Cancel = ReferenceEquals(e.Node, spine);
        tree.NodeEdited += (_, e) => e.Cancel = e.Text.Length == 0;

        Assert.False(tree.BeginEdit(spine));

        tree.BeginEdit(head);
        tree.Children.OfType<TextBox>().Single().Text = string.Empty;
        HeadlessInput.PressKey(form, Key.Enter);

        Assert.Equal("Head", head.Content);
    }

    [Fact]
    public void AHandlerChangesTheName()
    {
        var (_, tree, _, _, head, _) = Create();

        tree.NodeEditing += (_, e) => e.Text = "Bone_" + e.Text;
        tree.NodeEdited += (_, e) => e.Text = e.Text.Trim();

        tree.BeginEdit(head);

        TextBox editor = tree.Children.OfType<TextBox>().Single();
        Assert.Equal("Bone_Head", editor.Text);

        editor.Text = "  Bone_Skull ";
        tree.EndEdit(cancel: false);

        Assert.Equal("Bone_Skull", head.Content);
    }

    [Fact]
    public void F2RenamesWhenAllowed()
    {
        var (form, tree, _, _, head, _) = Create();

        tree.SelectedNode = head;
        tree.Focus();

        HeadlessInput.PressKey(form, Key.F2);
        Assert.Null(tree.EditingNode);

        tree.LabelEdit = true;
        HeadlessInput.PressKey(form, Key.F2);
        Assert.Same(head, tree.EditingNode);
    }

    [Fact]
    public void TheEditorStandsOverTheCaption()
    {
        var (form, tree, _, _, head, _) = Create();

        tree.BeginEdit(head);
        form.UpdateLayout();

        TextBox editor = tree.Children.OfType<TextBox>().Single();
        Point caption = Caption(tree, head);
        Point at = editor.GetAbsolutePosition();

        Assert.InRange(caption.X - at.X, 0f, 8f);
        Assert.InRange(caption.Y, at.Y, at.Y + editor.ActualSize.Height);
    }

    [Fact]
    public void TheEditorFollowsItsRowWhenTheTreeScrolls()
    {
        var tree = new TreeView { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };

        for (int i = 0; i < 50; i++)
            tree.Nodes.Add(new TreeNode($"Bone {i}"));

        var form = new Form { Size = new Size(300, 200), Content = tree };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        TreeNode node = tree.Nodes[3];
        tree.BeginEdit(node);
        form.UpdateLayout();

        VirtualizingStackPanel panel = tree.Children.OfType<VirtualizingStackPanel>().Single();
        panel.ScrollTo(0, tree.ItemHeight * 2);
        form.UpdateLayout();

        TextBox editor = tree.Children.OfType<TextBox>().Single();

        Assert.Equal(Row(tree, node).GetAbsolutePosition().Y, editor.GetAbsolutePosition().Y, 0.5f);
    }

    [Fact]
    public void ARemovedNodeEndsItsEdit()
    {
        var (_, tree, _, spine, head, _) = Create();

        tree.BeginEdit(head);
        spine.Children.Remove(head);

        Assert.Null(tree.EditingNode);
        Assert.Empty(tree.Children.OfType<TextBox>());
    }

    // ===== scrolling to a node =====

    [Fact]
    public void ANodeFoundInACollapsedBranchIsScrolledTo()
    {
        var tree = new TreeView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // twenty collapsed groups of ten: the last node is far below a 200 px tree
        TreeNode? wanted = null;

        for (int g = 0; g < 20; g++)
        {
            var group = new TreeNode($"Group {g}");

            for (int i = 0; i < 10; i++)
            {
                var node = new TreeNode($"Item {g}.{i}");
                group.Children.Add(node);
                wanted = node;
            }

            tree.Nodes.Add(group);
        }

        var form = new Form { Size = new Size(300, 200), Content = tree };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        // as the example's "Find" does: open the path, then select —
        // the rows of the opened branch are not measured yet
        wanted!.ExpandAncestors();
        tree.SelectedNode = wanted;
        form.UpdateLayout();

        // the whole row is inside the tree, not cut by its bottom edge
        TreeViewItem row = Row(tree, wanted);
        float top = row.GetAbsolutePosition().Y;
        float treeTop = tree.GetAbsolutePosition().Y;

        Assert.InRange(top, treeTop, treeTop + tree.ActualSize.Height - row.ActualSize.Height + 0.5f);
    }
}
