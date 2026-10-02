using System.Runtime.CompilerServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Tree;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A tree. Its items come from the node model, not from the realized rows:
/// a screen reader walks the whole expanded tree, while only the visible part of it
/// has rows.</summary>
public class TreeViewPeer : UIElementPeer, IAccessibilityChildFactory
{
    private readonly TreeView _tree;

    /// <summary>One peer per node for as long as the node lives: bridges know
    /// peers by reference, and a node scrolled out and back must stay the same.</summary>
    private readonly ConditionalWeakTable<TreeNode, TreeNodePeer> _nodes = new();

    public TreeViewPeer(TreeView owner) : base(owner)
    {
        _tree = owner;

        owner.SelectionChanged += (_, _) =>
        {
            if (FocusedDescendant is { } current)
                AccessibilityEvents.RaiseFocusChanged(current);
        };
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.Tree;

    public override IReadOnlyList<AccessibilityPeer> Children =>
        [.. _tree.Nodes.Select(PeerFor)];

    public override AccessibilityPeer? FocusedDescendant =>
        _tree is { IsFocused: true, SelectedNode: { } node } ? PeerFor(node) : null;

    internal TreeNodePeer PeerFor(TreeNode node) =>
        _nodes.GetValue(node, n => new TreeNodePeer(n, _tree, this));

    /// <summary>A realized row is its node: asked from the row, the answer is
    /// the same peer the model gives.</summary>
    AccessibilityPeer? IAccessibilityChildFactory.CreatePeerForChild(UIElement child) =>
        child is TreeViewItem item ? PeerFor(item.Node) : child.CreateDefaultAccessibilityPeer();
}

/// <summary>A node of a tree, realized as a row or not.</summary>
public class TreeNodePeer : AccessibilityPeer
{
    private readonly TreeNode _node;
    private readonly TreeView _tree;
    private readonly TreeViewPeer _treePeer;

    internal TreeNodePeer(TreeNode node, TreeView tree, TreeViewPeer treePeer)
    {
        _node = node;
        _tree = tree;
        _treePeer = treePeer;
    }

    public override AccessibilityRole Role => AccessibilityRole.TreeItem;

    public override string Name => _tree.GetItemText(_node);

    public override AccessibilityStates States
    {
        get
        {
            AccessibilityStates states = AccessibilityStates.Selectable;

            if (_node.HasChildren)
                states |= _node.IsExpanded ? AccessibilityStates.Expanded : AccessibilityStates.Collapsed;

            if (ReferenceEquals(_tree.SelectedNode, _node))
            {
                states |= AccessibilityStates.Selected;
                if (_tree.IsFocused) states |= AccessibilityStates.Focused;
            }

            if (!_tree.IsEffectivelyEnabled) states |= AccessibilityStates.Disabled;

            return states;
        }
    }

    public override AccessibilityActions Actions =>
        AccessibilityActions.Select | AccessibilityActions.ScrollIntoView |
        (_node.HasChildren
            ? _node.IsExpanded ? AccessibilityActions.Collapse : AccessibilityActions.Expand
            : AccessibilityActions.None);

    public override int Level
    {
        get
        {
            int level = 1;

            for (TreeNode node = _node; !IsTopLevel(node) && node.Parent is not null; node = node.Parent)
                level++;

            return level;
        }
    }

    public override SetPosition? Position
    {
        get
        {
            IList<TreeNode> siblings = IsTopLevel(_node) ? _tree.Nodes : _node.Parent!.Children;

            return new SetPosition(siblings.IndexOf(_node) + 1, siblings.Count);
        }
    }

    public override AccessibilityPeer? Parent =>
        IsTopLevel(_node) || _node.Parent is null ? _treePeer : _treePeer.PeerFor(_node.Parent);

    /// <summary>A collapsed node shows no children, and announces none.</summary>
    public override IReadOnlyList<AccessibilityPeer> Children =>
        _node.IsExpanded ? [.. _node.Children.Select(_treePeer.PeerFor)] : [];

    /// <summary>The row's bounds while it is realized; outside the viewport a node
    /// has none — an empty rectangle is how bridges learn it is off screen.</summary>
    public override Rectangle Bounds
    {
        get
        {
            foreach (UIElement child in _tree.Children)
            {
                if (child is TreeViewItem item && ReferenceEquals(item.Node, _node))
                    return new Rectangle(item.GetAbsolutePosition(), item.ActualSize);
            }

            return default;
        }
    }

    public override Form? Form => _tree.FindOwner();

    public override bool Select()
    {
        if (!_tree.IsEffectivelyEnabled) return false;

        _tree.SelectedNode = _node;
        return true;
    }

    public override bool Expand()
    {
        if (!_node.HasChildren || _node.IsExpanded) return false;

        _node.Expand();
        RaisePropertyChanged(AccessibilityProperty.States);
        RaiseStructureChanged();
        return true;
    }

    public override bool Collapse()
    {
        if (!_node.HasChildren || !_node.IsExpanded) return false;

        _node.Collapse();
        RaisePropertyChanged(AccessibilityProperty.States);
        RaiseStructureChanged();
        return true;
    }

    public override bool ScrollIntoView()
    {
        _tree.ScrollIntoView(_node);
        return true;
    }

    /// <summary>A top-level node: its parent is the tree's hidden root.</summary>
    private bool IsTopLevel(TreeNode node) => _tree.Nodes.Contains(node);
}