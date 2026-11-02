using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Tree;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.20: a tree made from a view model's items (<see cref="TreeView.ItemsSource"/>):
/// the nodes follow the collections and the items, both ways for expansion and checks, and
/// only while the tree is in a form.</summary>
[Collection("Platform")]
public class TreeItemsSourceTests
{
    private sealed class Folder(string name) : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? _propertyChanged;

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => _propertyChanged += value;
            remove => _propertyChanged -= value;
        }

        /// <summary>The handlers there are: an unsubscription of one that isn't there
        /// changes nothing, as with any event.</summary>
        public int Subscribers => _propertyChanged?.GetInvocationList().Length ?? 0;

        public string Name
        {
            get => field;
            set { field = value; Raise(); }
        } = name;

        public ObservableCollection<Folder> Children { get; } = [];

        public bool IsOpen
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                Raise();
            }
        }

        public bool IsIncluded
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                Raise();
            }
        }

        private void Raise([CallerMemberName] string? property = null) =>
            _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

        public override string ToString() => Name;
    }

    /// <summary>"Docs" with "Work" (with "Old") and "Home"; "Empty" with nothing.</summary>
    private static ObservableCollection<Folder> Folders(out Folder docs, out Folder work, out Folder home, out Folder empty)
    {
        work = new Folder("Work") { Children = { new Folder("Old") } };
        home = new Folder("Home");
        docs = new Folder("Docs") { Children = { work, home } };
        empty = new Folder("Empty");

        return [docs, empty];
    }

    private static (Form Form, TreeView Tree) Show(TreeItemsSource source, Action<TreeView>? setup = null)
    {
        var tree = new TreeView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        setup?.Invoke(tree);
        tree.ItemsSource = source;

        var form = new Form { Size = new Size(400, 300), Content = tree };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, tree);
    }

    private static TreeNode NodeOf(IEnumerable<TreeNode> level, Folder folder) =>
        level.Single(node => ReferenceEquals(node.Content, folder));

    [Fact]
    public void TheItemsBecomeNodesAsFarAsTheyAreOpened()
    {
        ObservableCollection<Folder> roots = Folders(out Folder docs, out Folder work, out _, out Folder empty);
        var (_, tree) = Show(TreeItemsSource.Create(roots, f => f.Children));

        Assert.Equal([docs, empty], tree.Nodes.Select(node => node.Content));

        TreeNode docsNode = NodeOf(tree.Nodes, docs);

        // the expander shows, the children aren't made until the node opens
        Assert.True(docsNode.HasChildren);
        Assert.Empty(docsNode.Children);
        Assert.False(NodeOf(tree.Nodes, empty).HasChildren);

        docsNode.Expand();

        Assert.Equal(2, docsNode.Children.Count);
        Assert.True(NodeOf(docsNode.Children, work).HasChildren);
        Assert.Empty(NodeOf(docsNode.Children, work).Children);
    }

    [Fact]
    public void TheNodesFollowTheCollections()
    {
        ObservableCollection<Folder> roots = Folders(out Folder docs, out _, out Folder home, out Folder empty);
        var (_, tree) = Show(TreeItemsSource.Create(roots, f => f.Children));

        TreeNode docsNode = NodeOf(tree.Nodes, docs);
        docsNode.Expand();
        TreeNode homeNode = NodeOf(docsNode.Children, home);

        var music = new Folder("Music");
        roots.Add(music);
        Assert.Equal([docs, empty, music], tree.Nodes.Select(node => node.Content));

        // moved, a node stays itself — expanded, with its children
        roots.Move(0, 2);
        Assert.Same(docsNode, tree.Nodes[2]);
        Assert.True(docsNode.IsExpanded);

        docs.Children.Remove(home);
        Assert.DoesNotContain(homeNode, docsNode.Children);
        Assert.Null(homeNode.Parent);

        // a node not opened yet: its expander follows whether there are children
        TreeNode emptyNode = NodeOf(tree.Nodes, empty);
        empty.Children.Add(new Folder("New"));
        Assert.True(emptyNode.HasChildren);
        Assert.Empty(emptyNode.Children);

        roots.Clear();
        Assert.Empty(tree.Nodes);
    }

    [Fact]
    public void ExpansionGoesBothWays()
    {
        ObservableCollection<Folder> roots = Folders(out Folder docs, out Folder work, out _, out _);
        docs.IsOpen = true;

        var (_, tree) = Show(TreeItemsSource.Create(roots, f => f.Children)
            .WithExpanded(f => f.IsOpen, (f, open) => f.IsOpen = open));

        TreeNode docsNode = NodeOf(tree.Nodes, docs);
        Assert.True(docsNode.IsExpanded);
        Assert.Equal(2, docsNode.Children.Count);

        docsNode.Collapse();
        Assert.False(docs.IsOpen);

        work.IsOpen = true;
        docs.IsOpen = true;

        Assert.True(docsNode.IsExpanded);
        Assert.True(NodeOf(docsNode.Children, work).IsExpanded);
    }

    [Fact]
    public void ChecksGoBothWaysAndTheItemsStatesAreTheTruth()
    {
        ObservableCollection<Folder> roots = Folders(out Folder docs, out Folder work, out Folder home, out _);
        docs.IsOpen = true;
        home.IsIncluded = true;

        var (_, tree) = Show(
            TreeItemsSource.Create(roots, f => f.Children)
                .WithExpanded(f => f.IsOpen, (f, open) => f.IsOpen = open)
                .WithChecked(f => f.IsIncluded, (f, included) => f.IsIncluded = included),
            t => t.CheckBoxes = true);

        TreeNode docsNode = NodeOf(tree.Nodes, docs);

        // Home's own state stays, and Docs shows its children: only one of two
        Assert.True(NodeOf(docsNode.Children, home).IsChecked);
        Assert.Equal(CheckedState.Intermediate, docsNode.CheckedState);
        Assert.False(docs.IsIncluded);

        // the user checks Docs: the cascade reaches every item
        docsNode.IsChecked = true;

        Assert.True(docs.IsIncluded);
        Assert.True(work.IsIncluded);
        Assert.True(home.IsIncluded);

        // an item changed in the view model: the tree follows, Docs shows it
        home.IsIncluded = false;

        Assert.False(NodeOf(docsNode.Children, home).IsChecked);
        Assert.Equal(CheckedState.Intermediate, docsNode.CheckedState);
        Assert.False(docs.IsIncluded);
    }

    [Fact]
    public void OutOfTheFormTheTreeHoldsNothingOfTheData()
    {
        ObservableCollection<Folder> roots = Folders(out Folder docs, out _, out _, out _);
        var source = TreeItemsSource.Create(roots, f => f.Children);
        var (form, tree) = Show(source);

        Assert.Equal(1, docs.Subscribers);

        form.Content = null;
        Assert.Equal(0, docs.Subscribers);

        // a change made while nobody listens shows when the tree comes back
        var music = new Folder("Music");
        roots.Add(music);
        Assert.Equal(2, tree.Nodes.Count);

        form.Content = tree;
        form.UpdateLayout();

        Assert.Equal(3, tree.Nodes.Count);
        Assert.Equal(1, docs.Subscribers);
    }

    [Fact]
    public void AChangedItemIsDrawnAgain()
    {
        ObservableCollection<Folder> roots = Folders(out Folder docs, out _, out _, out _);
        var (form, _) = Show(TreeItemsSource.Create(roots, f => f.Children));
        var window = (HeadlessWindow)form.PlatformWindow!;

        int before = window.InvalidateCount;
        docs.Name = "Documents";

        Assert.True(window.InvalidateCount > before, "the renamed item was not drawn");
    }

    [Fact]
    public void ANewSourceTakesThePlaceOfTheNodes()
    {
        ObservableCollection<Folder> roots = Folders(out Folder docs, out _, out _, out _);
        var first = TreeItemsSource.Create(roots, f => f.Children);
        var (_, tree) = Show(first);

        tree.ItemsSource = TreeItemsSource.Create(new[] { new Folder("Other") }, f => f.Children);

        Assert.Equal("Other", tree.Nodes.Single().Content!.ToString());
        Assert.Equal(0, docs.Subscribers);

        tree.ItemsSource = null;
        Assert.Empty(tree.Nodes);
    }

    [Fact]
    public void ClearedChildrenAreFreeToGoElsewhere()
    {
        var child = new TreeNode("child");
        var first = new TreeNode("first") { Children = { child } };
        var second = new TreeNode("second");

        first.Children.Clear();

        Assert.Null(child.Parent);

        // it used to keep its old parent, and the contract refused this
        second.Children.Add(child);
        Assert.Same(second, child.Parent);

        second.Children.ReplaceAll([new TreeNode("other")]);
        Assert.Null(child.Parent);
    }
}
