namespace ZeppelinForms.Forms.Controls.Tree;

/// <summary>What checking a node does to the rest of the tree, see
/// <see cref="TreeView.CheckPropagation"/>.</summary>
public enum CheckPropagation : byte
{
    /// <summary>A node checked or unchecked takes its whole subtree with it, and a parent
    /// shows what its children are: checked when all of them are, unchecked when none
    /// is, <see cref="CheckedState.Intermediate"/> in between. Explorer's and most
    /// installers' trees work this way.</summary>
    Cascade,

    /// <summary>Each node keeps its own state, and the children are left as they are; a
    /// parent that is not checked itself shows <see cref="CheckedState.Intermediate"/>
    /// while a node inside it is checked — the "something is chosen in here" of a
    /// layer or a filter tree.</summary>
    Indicate,

    /// <summary>Each node keeps its own state, and nothing shows the others: the tree
    /// before 0.20.</summary>
    None,
}

/// <summary>The check boxes of a tree: how a change goes through it, and the one event it
/// gives per change however many nodes it took along.</summary>
public partial class TreeView
{
    /// <summary>What checking a node does to the rest of the tree. <see cref="CheckPropagation.Cascade"/>
    /// by default — until 0.20 every node kept its own state, as <see cref="CheckPropagation.None"/>
    /// does now.</summary>
    /// <remarks>A change puts the whole tree in order by the new rule at once: a parent
    /// shows its children under Cascade, an unchecked parent of a checked node shows
    /// the third state under Indicate.</remarks>
    public CheckPropagation CheckPropagation
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            if (value == CheckPropagation.None) return;

            InCheckAction(_root, () => Propagating(() =>
            {
                foreach (TreeNode top in _root.Children)
                    NormalizeSubtree(top);
            }));
        }
    } = CheckPropagation.Cascade;

    /// <summary>Nodes were checked or unchecked: once per change, with every node it
    /// changed — the node clicked and its subtree under <see cref="CheckPropagation.Cascade"/>,
    /// with the parents that now show it.</summary>
    /// <remarks>Until 0.20 it was raised per node, with the node as its argument: a
    /// subtree of ten thousand nodes checked at once raised it ten thousand times.</remarks>
    public event EventHandler<TreeNodeCheckedEventArgs>? NodeCheckedChanged;

    /// <summary>The nodes a check action changed so far; null — none is running.</summary>
    private List<TreeNode>? _checkChanges;

    /// <summary>Where the running check action started.</summary>
    private TreeNode? _checkInitiator;

    /// <summary>The tree is changing states itself: a change that comes now is one of its
    /// own — written down, not propagated again.</summary>
    private bool _propagating;

    /// <summary>Parents whose children changed during <see cref="BeginUpdate"/>: their
    /// check boxes are put in order at the last <see cref="EndUpdate"/>, once — a
    /// thousand children added one by one would read all their siblings a thousand times.</summary>
    private readonly HashSet<TreeNode> _checksPending = [];

    /// <summary>A node's state changed — from its check box, Space, code, or the tree
    /// itself in the middle of a propagation.</summary>
    private void OnNodeCheckedChanged(TreeNode node)
    {
        if (_propagating)
        {
            _checkChanges?.Add(node);
            return;
        }

        InCheckAction(node, () =>
        {
            _checkChanges!.Add(node);
            Propagating(() => Propagate(node));
        });
    }

    /// <summary>Children were added to a node or removed from it: the new ones take the
    /// rule — under Cascade a parent shows them — and the parent and its ancestors show
    /// what changed.</summary>
    private void OnNodeChildrenChanged(TreeNode parent, System.Collections.IList? added)
    {
        if (CheckPropagation == CheckPropagation.None) return;

        if (_updateDepth > 0)
        {
            _checksPending.Add(parent);
            return;
        }

        InCheckAction(parent, () => Propagating(() =>
        {
            if (added is not null)
                foreach (TreeNode child in added)
                    NormalizeSubtree(child);

            RecomputeUp(parent);
        }));
    }

    /// <summary>The last <see cref="EndUpdate"/>: the parents whose children changed in
    /// between are put in order, each with its subtree.</summary>
    private void FlushPendingChecks()
    {
        if (_checksPending.Count == 0) return;

        TreeNode[] parents = [.. _checksPending];
        _checksPending.Clear();

        if (CheckPropagation == CheckPropagation.None) return;

        InCheckAction(parents[0], () => Propagating(() =>
        {
            foreach (TreeNode parent in parents)
            {
                // removed from the tree meanwhile: nothing of it is shown
                if (!IsInTree(parent) && !ReferenceEquals(parent, _root)) continue;

                if (ReferenceEquals(parent, _root))
                {
                    foreach (TreeNode top in _root.Children)
                        NormalizeSubtree(top);

                    continue;
                }

                // the parent is in order with its subtree now: from its own parent up —
                // started at the parent itself, the walk would stop at once, finding it
                // as it should be
                NormalizeSubtree(parent);
                RecomputeUp(parent.Parent);
            }
        }));
    }

    /// <summary>Under Cascade a node expanded for the first time takes the children it
    /// made into its own state: a checked folder read now has its files checked, rather
    /// than turning unchecked because they came unchecked.</summary>
    /// <param name="before">The node's state before <see cref="NodeExpanding"/> ran.</param>
    private void CascadeIntoNewChildren(TreeNode node, CheckedState before)
    {
        if (CheckPropagation != CheckPropagation.Cascade || before == CheckedState.Intermediate) return;

        // the pending put-in-order of this node would read the new children as they
        // came; their parent decides instead
        _checksPending.Remove(node);

        InCheckAction(node, () => Propagating(() =>
        {
            node.SetCheckedState(before);
            SetDescendants(node, before);
            RecomputeUp(node.Parent);
        }));
    }

    /// <summary>Check or uncheck the selected nodes together, by the lead's state:
    /// what the lead becomes, all of them become.</summary>
    /// <remarks>One action: <see cref="NodeCheckedChanged"/> comes once, with every
    /// node the keys changed.</remarks>
    private void ToggleCheckedSelection()
    {
        if (SelectedNode is not { } lead) return;

        bool check = !lead.IsChecked;

        InCheckAction(lead, () =>
        {
            foreach (TreeNode node in SelectedNodes)
                node.IsChecked = check;

            // the lead even if it isn't in the set — a node can lead an emptied selection
            lead.IsChecked = check;
        });
    }

    /// <summary>Run a change of check boxes as one: the nodes it changes are collected,
    /// and <see cref="NodeCheckedChanged"/> is raised once at the end. Nested ones join
    /// the outer.</summary>
    private void InCheckAction(TreeNode initiator, Action action)
    {
        if (_checkChanges is not null)
        {
            action();
            return;
        }

        _checkChanges = [];
        _checkInitiator = initiator;

        List<TreeNode> changes;
        TreeNode start;

        try
        {
            action();
        }
        finally
        {
            changes = _checkChanges;
            start = _checkInitiator;
            _checkChanges = null;
            _checkInitiator = null;
        }

        if (changes.Count == 0) return;

        // the rows show the states the action left, once: OnTreeChanged let them be meanwhile
        SyncRows();
        InvalidateVisual();

        // a node the action went through twice — unchecked by its parent's cascade, then
        // shown as Intermediate — is told once, as it is now
        var seen = new HashSet<TreeNode>(changes.Count);
        List<TreeNode> distinct = new(changes.Count);

        foreach (TreeNode node in changes)
            if (seen.Add(node)) distinct.Add(node);

        NodeCheckedChanged?.Invoke(this, new TreeNodeCheckedEventArgs(start, distinct));
    }

    private void Propagating(Action action)
    {
        bool was = _propagating;
        _propagating = true;

        try
        {
            action();
        }
        finally
        {
            _propagating = was;
        }
    }

    /// <summary>What a node's change does to the others, by the rule.</summary>
    private void Propagate(TreeNode node)
    {
        switch (CheckPropagation)
        {
            case CheckPropagation.Cascade:
                // the third state is not a choice to pass down: a parent given it
                // shows its children instead, a leaf keeps it
                if (node.CheckedState != CheckedState.Intermediate)
                    SetDescendants(node, node.CheckedState);
                else if (node.Children.Count > 0)
                    node.SetCheckedState(Derive(node));

                break;

            case CheckPropagation.Indicate:
                // unchecked with something checked inside: the third state says so
                if (node.Children.Count > 0 && node.CheckedState != CheckedState.Checked)
                    node.SetCheckedState(Derive(node));

                break;

            default:
                return;
        }

        RecomputeUp(node.Parent);
    }

    /// <summary>The state of every descendant, iteratively: the depth is the data's.</summary>
    private static void SetDescendants(TreeNode node, CheckedState state)
    {
        Stack<TreeNode> pending = new();

        foreach (TreeNode child in node.Children)
            pending.Push(child);

        while (pending.Count > 0)
        {
            TreeNode current = pending.Pop();

            current.SetCheckedState(state);

            foreach (TreeNode child in current.Children)
                pending.Push(child);
        }
    }

    /// <summary>The ancestors from this node up show what is below them. Stops at the
    /// first that stays as it was: those above it read the same children as before.</summary>
    private void RecomputeUp(TreeNode? from)
    {
        for (TreeNode? node = from; node is not null && !ReferenceEquals(node, _root); node = node.Parent)
        {
            CheckedState state = Derive(node);

            if (state == node.CheckedState) return;

            node.SetCheckedState(state);
        }
    }

    /// <summary>A subtree in order by the rule, children before parents: a parent is read
    /// from children already in order. The leaves keep their own states.</summary>
    private void NormalizeSubtree(TreeNode top)
    {
        // pre-order, then walked backwards: every child comes before its parent
        List<TreeNode> order = [];
        Stack<TreeNode> pending = new();
        pending.Push(top);

        while (pending.Count > 0)
        {
            TreeNode node = pending.Pop();
            order.Add(node);

            foreach (TreeNode child in node.Children)
                pending.Push(child);
        }

        for (int i = order.Count - 1; i >= 0; i--)
        {
            TreeNode node = order[i];

            if (node.Children.Count > 0)
                node.SetCheckedState(Derive(node));
        }
    }

    /// <summary>The state a parent shows by the rule. A node left without children — the
    /// last one removed — drops the third state it had for them.</summary>
    private CheckedState Derive(TreeNode node)
    {
        if (node.Children.Count == 0)
            return node.CheckedState == CheckedState.Intermediate ? CheckedState.Unchecked : node.CheckedState;

        if (CheckPropagation == CheckPropagation.Indicate)
        {
            // checked is the node's own; the third state only says what is inside
            if (node.CheckedState == CheckedState.Checked) return CheckedState.Checked;

            foreach (TreeNode child in node.Children)
                if (child.CheckedState != CheckedState.Unchecked)
                    return CheckedState.Intermediate;

            return CheckedState.Unchecked;
        }

        bool anyChecked = false;
        bool anyUnchecked = false;

        foreach (TreeNode child in node.Children)
        {
            switch (child.CheckedState)
            {
                case CheckedState.Checked:
                    anyChecked = true;
                    break;

                case CheckedState.Unchecked:
                    anyUnchecked = true;
                    break;

                default:
                    return CheckedState.Intermediate;
            }

            if (anyChecked && anyUnchecked) return CheckedState.Intermediate;
        }

        return anyChecked ? CheckedState.Checked : CheckedState.Unchecked;
    }
}
