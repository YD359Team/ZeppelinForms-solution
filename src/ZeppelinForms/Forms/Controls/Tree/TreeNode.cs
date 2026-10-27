using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ZeppelinForms.Diagnostics;
using ZeppelinForms.Drawing;

namespace ZeppelinForms.Forms.Controls.Tree;

/// <summary>What exactly changed in a node.</summary>
public enum TreeChangeKind
{
    /// <summary>The set of visible rows is the same — only the content changed.</summary>
    Content,

    /// <summary>The list of visible rows must be rebuilt: expanding, collapsing,
    /// adding or removing children.</summary>
    Structure,
}

/// <summary>A tree node. The data lies in Content, the control is built from it.</summary>
public sealed class TreeNode
{
    public TreeNode()
    {
        Children.CollectionChanged += OnChildrenChanged;
    }

    public TreeNode(object? content) : this() => Content = content;

    public object? Content
    {
        get;
        set
        {
            if (Equals(field, value)) return;

            field = value;
            RaiseChanged(TreeChangeKind.Content);
        }
    }

    /// <summary>The icon before the caption — a bone, a helper, an emitter. Null — the
    /// tree's <see cref="TreeView.ItemIcon"/>, if it gives one, otherwise none.</summary>
    public IconSource? Icon
    {
        get;
        set
        {
            if (Equals(field, value)) return;

            field = value;
            RaiseChanged(TreeChangeKind.Content);
        }
    }

    /// <summary>The node's check box, shown while the tree's
    /// <see cref="TreeView.CheckBoxes"/> is on: visible, enabled, exported.</summary>
    /// <remarks>Each node keeps its own: checking a parent doesn't check its children,
    /// and a parent doesn't show what its children are. A bone hidden with its parent
    /// shown is a state of its own, not a third state of the parent.</remarks>
    public bool IsChecked
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            RaiseChanged(TreeChangeKind.Content);
            RaiseCheckedChanged();
        }
    }

    public ObservableCollection<TreeNode> Children { get; } = [];

    public TreeNode? Parent { get; private set; }

    public bool IsExpanded
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // before the rows are rebuilt: a handler that fills the children in place
            // gets them into the very rebuild the expansion makes
            if (value) RaiseExpanding();

            RaiseChanged(TreeChangeKind.Structure);
        }
    }

    /// <summary>The node has children that are not made yet — a folder not read, a
    /// record not loaded. It shows the expander as a node with children does, and
    /// <see cref="TreeView.NodeExpanding"/> is where the children are made.</summary>
    /// <remarks>Reset it once the children are known: a node that turned out to be
    /// empty loses the expander only then.</remarks>
    public bool HasUnrealizedChildren
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            RaiseChanged(TreeChangeKind.Content);
        }
    }

    public bool HasChildren => Children.Count > 0 || HasUnrealizedChildren;

    /// <summary>The depth from the root. Computed by walking up rather than stored:
    /// a stored field would have to be updated on every subtree move,
    /// and one day someone would forget to update it.</summary>
    public int Level
    {
        get
        {
            int level = 0;

            for (TreeNode? node = Parent; node is not null; node = node.Parent)
                level++;

            return level;
        }
    }

    /// <summary>A change in any node of the subtree. The event rises to the root
    /// and is raised only there: TreeView is subscribed to one node rather than
    /// to each, so removed subtrees don't need unsubscribing — and there is
    /// nothing to forget.</summary>
    internal event Action<TreeNode, TreeChangeKind>? Changed;

    /// <summary>A node of the subtree was checked or unchecked — raised at the root,
    /// as <see cref="Changed"/> is.</summary>
    internal event Action<TreeNode>? CheckedChanged;

    /// <summary>A node of the subtree is being expanded — raised at the root,
    /// as <see cref="Changed"/> is.</summary>
    internal event Action<TreeNode>? Expanding;

    public void Expand() => IsExpanded = true;

    public void Collapse() => IsExpanded = false;

    /// <summary>Expand all ancestors so that the node becomes visible.</summary>
    public void ExpandAncestors()
    {
        for (TreeNode? node = Parent; node is not null; node = node.Parent)
            node.IsExpanded = true;
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (TreeNode child in e.OldItems)
                if (ReferenceEquals(child.Parent, this))
                    child.Parent = null;

        if (e.NewItems is not null)
        {
            foreach (TreeNode child in e.NewItems)
            {
                // a node in two trees at once would give an endless projection:
                // the walk would go round in a circle through the shared descendant
                ZfContract.Require(child.Parent is null || ReferenceEquals(child.Parent, this),
                    "The node already belongs to another parent — remove it from there first.");

                child.Parent = this;
            }
        }

        RaiseChanged(TreeChangeKind.Structure);
    }

    private void RaiseExpanding()
    {
        TreeNode root = this;

        while (root.Parent is not null)
            root = root.Parent;

        root.Expanding?.Invoke(this);
    }

    private void RaiseCheckedChanged()
    {
        TreeNode root = this;

        while (root.Parent is not null)
            root = root.Parent;

        root.CheckedChanged?.Invoke(this);
    }

    private void RaiseChanged(TreeChangeKind kind)
    {
        TreeNode root = this;

        while (root.Parent is not null)
            root = root.Parent;

        root.Changed?.Invoke(this, kind);
    }
}