using System.ComponentModel;
using Xunit;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Controls.Tree;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>0.20: what checking a node does to the rest of the tree
/// (<see cref="TreeView.CheckPropagation"/>), the one event per change, and the node
/// telling its changes to bindings.</summary>
[Collection("Platform")]
public class TreeViewCheckPropagationTests
{
    /// <summary>"Root" with "A" and "B", "B" with "C" and "D", all expanded.</summary>
    private static (Form Form, TreeView Tree, TreeNode Root, TreeNode A, TreeNode B, TreeNode C, TreeNode D) Create(
        CheckPropagation mode = CheckPropagation.Cascade, Action<TreeView>? setup = null)
    {
        var tree = new TreeView
        {
            CheckBoxes = true,
            CheckPropagation = mode,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        setup?.Invoke(tree);

        var c = new TreeNode("C");
        var d = new TreeNode("D");
        var b = new TreeNode("B") { Children = { c, d } };
        var a = new TreeNode("A");
        var root = new TreeNode("Root") { Children = { a, b } };

        tree.Nodes.Add(root);
        tree.ExpandAll();

        var form = new Form { Size = new Size(400, 300), Content = tree };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, tree, root, a, b, c, d);
    }

    private static void AssertStates(params (TreeNode Node, CheckedState State)[] expected)
    {
        foreach ((TreeNode node, CheckedState state) in expected)
            Assert.True(node.CheckedState == state, $"{node.Content}: {node.CheckedState}, expected {state}");
    }

    // ===== Cascade =====

    [Fact]
    public void CascadeIsTheDefault() => Assert.Equal(CheckPropagation.Cascade, new TreeView().CheckPropagation);

    [Fact]
    public void CheckingAParentChecksItsWholeSubtree()
    {
        var (_, _, root, a, b, c, d) = Create();

        root.IsChecked = true;

        AssertStates((root, CheckedState.Checked), (a, CheckedState.Checked), (b, CheckedState.Checked),
            (c, CheckedState.Checked), (d, CheckedState.Checked));

        b.IsChecked = false;

        AssertStates((root, CheckedState.Intermediate), (a, CheckedState.Checked), (b, CheckedState.Unchecked),
            (c, CheckedState.Unchecked), (d, CheckedState.Unchecked));
    }

    [Fact]
    public void AParentShowsWhatItsChildrenAre()
    {
        var (_, _, root, a, b, c, d) = Create();

        c.IsChecked = true;
        AssertStates((b, CheckedState.Intermediate), (root, CheckedState.Intermediate));

        d.IsChecked = true;
        AssertStates((b, CheckedState.Checked), (root, CheckedState.Intermediate));

        a.IsChecked = true;
        AssertStates((root, CheckedState.Checked));

        c.IsChecked = false;
        AssertStates((b, CheckedState.Intermediate), (root, CheckedState.Intermediate));
    }

    [Fact]
    public void ClickingAnIntermediateParentChecksIt()
    {
        var (_, _, _, _, b, c, d) = Create();

        c.IsChecked = true;
        Assert.Equal(CheckedState.Intermediate, b.CheckedState);

        // what a click on the box does: the third state isn't "checked", so it checks
        b.IsChecked = !b.IsChecked;

        AssertStates((b, CheckedState.Checked), (c, CheckedState.Checked), (d, CheckedState.Checked));
    }

    [Fact]
    public void TheThirdStateSetOnAParentGivesWayToItsChildren()
    {
        var (_, _, _, _, b, c, d) = Create();

        c.IsChecked = true;
        d.IsChecked = true;

        b.CheckedState = CheckedState.Intermediate;

        // its children all agree: it shows them, and they are left as they were
        AssertStates((b, CheckedState.Checked), (c, CheckedState.Checked), (d, CheckedState.Checked));
    }

    [Fact]
    public void AChangeIsToldOnceWithEveryNodeItChanged()
    {
        var (_, tree, root, a, b, c, d) = Create();

        List<TreeNodeCheckedEventArgs> raised = [];
        tree.NodeCheckedChanged += (_, e) => raised.Add(e);

        root.IsChecked = true;

        TreeNodeCheckedEventArgs args = Assert.Single(raised);
        Assert.Same(root, args.Node);
        Assert.Same(root, args.ChangedNodes[0]);
        Assert.Equal(5, args.ChangedNodes.Count);
        Assert.True(args.ChangedNodes.ToHashSet().SetEquals([root, a, b, c, d]));
    }

    [Fact]
    public void ChildrenThatComeOrGoAreShownByTheirParent()
    {
        var (_, _, root, _, b, _, _) = Create();

        root.IsChecked = true;

        var e = new TreeNode("E");
        b.Children.Add(e);

        AssertStates((e, CheckedState.Unchecked), (b, CheckedState.Intermediate), (root, CheckedState.Intermediate));

        b.Children.Remove(e);

        AssertStates((b, CheckedState.Checked), (root, CheckedState.Checked));
    }

    [Fact]
    public void ChildrenAddedInAnUpdateAreReadOnceAtItsEnd()
    {
        var (_, tree, root, a, _, _, _) = Create();

        int events = 0;
        tree.NodeCheckedChanged += (_, _) => events++;

        tree.BeginUpdate();

        for (int i = 0; i < 3; i++)
            a.Children.Add(new TreeNode($"A{i}") { IsChecked = true });

        // nothing is put in order until the update ends
        Assert.Equal(CheckedState.Unchecked, a.CheckedState);
        Assert.Equal(0, events);

        tree.EndUpdate();

        AssertStates((a, CheckedState.Checked), (root, CheckedState.Intermediate));
        Assert.Equal(1, events);
    }

    [Fact]
    public void ANodeExpandedForTheFirstTimeChecksTheChildrenItMade()
    {
        var (_, tree, root, a, _, _, _) = Create();

        var folder = new TreeNode("Folder") { HasUnrealizedChildren = true };
        a.Children.Add(folder);

        tree.NodeExpanding += (_, node) =>
        {
            if (!node.HasUnrealizedChildren) return;

            node.Children.Add(new TreeNode("one"));
            node.Children.Add(new TreeNode("two"));
            node.HasUnrealizedChildren = false;
        };

        root.IsChecked = true;
        Assert.True(folder.IsChecked);

        folder.Expand();

        Assert.All(folder.Children, child => Assert.True(child.IsChecked));
        AssertStates((folder, CheckedState.Checked), (a, CheckedState.Checked), (root, CheckedState.Checked));
    }

    [Fact]
    public void SpaceChecksTheSelectionAsOneChange()
    {
        var (form, tree, root, a, b, c, _) = Create(setup: t => t.SelectionMode = SelectionMode.Multiple);

        List<TreeNodeCheckedEventArgs> raised = [];
        tree.NodeCheckedChanged += (_, e) => raised.Add(e);

        tree.SetSelected(a, true);
        tree.SetSelected(c, true);
        tree.Focus();

        HeadlessInput.PressKey(form, Key.Space);

        AssertStates((a, CheckedState.Checked), (c, CheckedState.Checked), (b, CheckedState.Intermediate),
            (root, CheckedState.Intermediate));

        TreeNodeCheckedEventArgs args = Assert.Single(raised);
        Assert.Same(c, args.Node);
        Assert.Contains(a, args.ChangedNodes);
        Assert.Contains(b, args.ChangedNodes);
    }

    [Fact]
    public void ANodeInTheThirdStateSaysSo()
    {
        var (_, tree, _, _, b, c, _) = Create();

        c.IsChecked = true;

        AccessibilityPeer rootPeer = tree.GetAccessibilityPeer()!.Children[0];
        AccessibilityPeer bPeer = rootPeer.Children.Single(peer => peer.Name == "B");

        Assert.True(bPeer.States.HasFlag(AccessibilityStates.Mixed));
        Assert.False(bPeer.States.HasFlag(AccessibilityStates.Checked));

        // a toggle from the screen reader checks it, as a click does
        Assert.True(bPeer.Toggle());
        Assert.True(b.IsChecked);
    }

    // ===== Indicate =====

    [Fact]
    public void IndicateLeavesTheChildrenAndShowsWhatIsInside()
    {
        var (_, _, root, _, b, c, d) = Create(CheckPropagation.Indicate);

        b.IsChecked = true;
        AssertStates((b, CheckedState.Checked), (c, CheckedState.Unchecked), (d, CheckedState.Unchecked),
            (root, CheckedState.Intermediate));

        c.IsChecked = true;

        // checked is B's own: something checked inside doesn't change it
        AssertStates((b, CheckedState.Checked), (root, CheckedState.Intermediate));

        b.IsChecked = false;
        AssertStates((b, CheckedState.Intermediate), (c, CheckedState.Checked), (root, CheckedState.Intermediate));

        c.IsChecked = false;
        AssertStates((b, CheckedState.Unchecked), (root, CheckedState.Unchecked));
    }

    // ===== None and switching =====

    [Fact]
    public void NoneLeavesEveryNodeByItself()
    {
        var (_, _, root, _, b, c, d) = Create(CheckPropagation.None);

        c.IsChecked = true;
        AssertStates((b, CheckedState.Unchecked), (root, CheckedState.Unchecked));

        root.IsChecked = true;
        AssertStates((b, CheckedState.Unchecked), (d, CheckedState.Unchecked));
    }

    [Fact]
    public void SwitchingTheRulePutsTheTreeInOrder()
    {
        var (_, tree, root, _, b, c, d) = Create(CheckPropagation.None);

        c.IsChecked = true;
        d.IsChecked = true;
        AssertStates((b, CheckedState.Unchecked));

        tree.CheckPropagation = CheckPropagation.Cascade;

        // B shows its children now; Root has A unchecked and B checked
        AssertStates((b, CheckedState.Checked), (root, CheckedState.Intermediate));

        c.IsChecked = false;
        tree.CheckPropagation = CheckPropagation.Indicate;

        // B was put in order by Cascade as Intermediate; under Indicate it is unchecked
        // itself with D checked inside, which reads the same
        AssertStates((b, CheckedState.Intermediate), (d, CheckedState.Checked), (root, CheckedState.Intermediate));
    }

    // ===== the node tells its changes =====

    [Fact]
    public void TheNodeTellsItsChanges()
    {
        var node = new TreeNode("x");
        List<string?> changed = [];
        ((INotifyPropertyChanged)node).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        node.IsExpanded = true;
        node.IsChecked = true;
        node.CheckedState = CheckedState.Intermediate;
        node.Content = "y";
        node.Children.Add(new TreeNode("first"));
        node.Children.Add(new TreeNode("second"));

        Assert.Equal(
            [
                nameof(TreeNode.IsExpanded),
                nameof(TreeNode.CheckedState), nameof(TreeNode.IsChecked),
                // Checked to Intermediate: IsChecked turns false as well
                nameof(TreeNode.CheckedState), nameof(TreeNode.IsChecked),
                nameof(TreeNode.Content),
                // only the first child changes whether there are children
                nameof(TreeNode.HasChildren),
            ],
            changed);
    }

    [Fact]
    public void BindingsFollowTheNodeAndItsChildren()
    {
        var node = new TreeNode("x");
        var state = new Label();
        var count = new Label();

        state.Bind(Label.TextProperty, node, n => n.IsExpanded ? "open" : "closed");
        count.Bind(Label.TextProperty, node.Children, children => children.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal("closed", state.Text);
        Assert.Equal("0", count.Text);

        node.Expand();
        node.Children.Add(new TreeNode("child"));

        Assert.Equal("open", state.Text);
        Assert.Equal("1", count.Text);
    }
}
