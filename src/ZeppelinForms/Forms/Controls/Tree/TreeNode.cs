﻿using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
/// <remarks>Since 0.20 the node raises <see cref="PropertyChanged"/>: a binding to
/// <see cref="IsExpanded"/>, <see cref="CheckedState"/> or <see cref="Content"/> follows
/// the node, and one to <c>Children.Count</c> follows its children — the collection
/// raises its own.</remarks>
public sealed class TreeNode : INotifyPropertyChanged
{
    public TreeNode()
    {
        Children.CollectionChanged += OnChildrenChanged;
    }

    public TreeNode(object? content) : this() => Content = content;

    /// <summary>A property of the node changed: <see cref="Content"/>, <see cref="Icon"/>,
    /// <see cref="CheckedState"/> and <see cref="IsChecked"/>, <see cref="IsExpanded"/>,
    /// <see cref="HasUnrealizedChildren"/>, <see cref="HasChildren"/>, <see cref="Parent"/>.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    public object? Content
    {
        get;
        set
        {
            if (Equals(field, value)) return;

            field = value;
            RaiseChanged(TreeChangeKind.Content);
            Notify(s_content);
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
            Notify(s_icon);
        }
    }

    /// <summary>The node's check box, shown while the tree's
    /// <see cref="TreeView.CheckBoxes"/> is on: visible, enabled, exported. True only
    /// when it is <see cref="CheckedState.Checked"/>; setting it gives Checked or Unchecked.</summary>
    /// <remarks>What a change does to the rest of the tree — the children checked with
    /// their parent, a parent showing what its children are — is the tree's
    /// <see cref="TreeView.CheckPropagation"/>.</remarks>
    public bool IsChecked
    {
        get => CheckedState == CheckedState.Checked;
        set => CheckedState = value ? CheckedState.Checked : CheckedState.Unchecked;
    }

    /// <summary>The state of the node's check box, the third one included: a parent some
    /// of whose children are checked, see <see cref="TreeView.CheckPropagation"/>.</summary>
    /// <remarks>The tree may change the state it was given at once: under
    /// <see cref="CheckPropagation.Cascade"/> a parent shows its children, so
    /// <see cref="CheckedState.Intermediate"/> set on one whose children all agree gives
    /// way to what they are; under <see cref="CheckPropagation.Indicate"/> a parent
    /// unchecked with a checked node inside shows Intermediate.</remarks>
    public CheckedState CheckedState
    {
        get => _checkedState;
        set => SetCheckedState(value);
    }

    private CheckedState _checkedState;

    /// <summary>Change the state. The tree's propagation goes through here as well: it
    /// tells its own changes apart from the others by when they come, see TreeView.</summary>
    internal void SetCheckedState(CheckedState value)
    {
        if (_checkedState == value) return;

        bool wasChecked = _checkedState == CheckedState.Checked;

        _checkedState = value;

        RaiseChanged(TreeChangeKind.Content);
        Notify(s_checkedState);

        if (wasChecked != (value == CheckedState.Checked))
            Notify(s_isChecked);

        RaiseCheckedChanged();
    }

    public ObservableCollection<TreeNode> Children { get; } = [];

    public TreeNode? Parent
    {
        get;
        private set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;
            Notify(s_parent);
        }
    }

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
            Notify(s_isExpanded);
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
            Notify(s_hasUnrealizedChildren);
            NotifyHasChildren();
        }
    }

    public bool HasChildren => Children.Count > 0 || HasUnrealizedChildren;

    /// <summary><see cref="HasChildren"/> as last announced: it is computed, and a change
    /// of it is told only when it really flips — not on every child added.</summary>
    private bool _hadChildren;

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

    /// <summary>The children of a node of the subtree changed — raised at the root, as
    /// <see cref="Changed"/> is, with the node and the children added (null — none,
    /// or the collection was reset). Before the <see cref="TreeChangeKind.Structure"/>
    /// change it comes with: the check boxes of the new children are put in order
    /// before the rows are rebuilt.</summary>
    internal event Action<TreeNode, IList?>? ChildrenChanged;

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

        RaiseChildrenChanged(e.NewItems);
        RaiseChanged(TreeChangeKind.Structure);
        NotifyHasChildren();
    }

    private void NotifyHasChildren()
    {
        bool has = HasChildren;

        if (has == _hadChildren) return;

        _hadChildren = has;
        Notify(s_hasChildren);
    }

    private void Notify(PropertyChangedEventArgs args) => PropertyChanged?.Invoke(this, args);

    private TreeNode Root
    {
        get
        {
            TreeNode root = this;

            while (root.Parent is not null)
                root = root.Parent;

            return root;
        }
    }

    private void RaiseExpanding() => Root.Expanding?.Invoke(this);

    private void RaiseCheckedChanged() => Root.CheckedChanged?.Invoke(this);

    private void RaiseChildrenChanged(IList? added) => Root.ChildrenChanged?.Invoke(this, added);

    private void RaiseChanged(TreeChangeKind kind) => Root.Changed?.Invoke(this, kind);

    // made once: a tree of thousands of nodes would otherwise make an argument per change
    private static readonly PropertyChangedEventArgs s_content = new(nameof(Content));
    private static readonly PropertyChangedEventArgs s_icon = new(nameof(Icon));
    private static readonly PropertyChangedEventArgs s_isChecked = new(nameof(IsChecked));
    private static readonly PropertyChangedEventArgs s_checkedState = new(nameof(CheckedState));
    private static readonly PropertyChangedEventArgs s_parent = new(nameof(Parent));
    private static readonly PropertyChangedEventArgs s_isExpanded = new(nameof(IsExpanded));
    private static readonly PropertyChangedEventArgs s_hasUnrealizedChildren = new(nameof(HasUnrealizedChildren));
    private static readonly PropertyChangedEventArgs s_hasChildren = new(nameof(HasChildren));
}
