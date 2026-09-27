using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ZeppelinForms.Diagnostics;

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

    public ObservableCollection<TreeNode> Children { get; } = [];

    public TreeNode? Parent { get; private set; }

    public bool IsExpanded
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            RaiseChanged(TreeChangeKind.Structure);
        }
    }

    public bool HasChildren => Children.Count > 0;

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

    private void RaiseChanged(TreeChangeKind kind)
    {
        TreeNode root = this;

        while (root.Parent is not null)
            root = root.Parent;

        root.Changed?.Invoke(this, kind);
    }
}