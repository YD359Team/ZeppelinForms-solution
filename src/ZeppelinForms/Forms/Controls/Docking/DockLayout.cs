using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Docking;

// The layout of a DockHost as a tree: splits divide their space among their children by
// weight, groups are the leaves and hold the items as tabs. The views are made from it
// (DockHost.SyncViews); the tree itself knows nothing of elements but the view it keeps.

/// <summary>A node of the layout: a split or a group.</summary>
internal abstract class DockNode
{
    public DockSplit? Parent { get; set; }

    /// <summary>The node's share of its parent's extent, against its siblings' weights.</summary>
    public float Weight { get; set; } = 1f;

    /// <summary>The node's view, once it was made.</summary>
    public abstract UIElement? Element { get; }
}

/// <summary>Children side by side (<see cref="Orientation.Horizontal"/>) or one under the
/// other, with a splitter between each two.</summary>
internal sealed class DockSplit(Orientation orientation) : DockNode
{
    public Orientation Orientation { get; } = orientation;

    public List<DockNode> Children { get; } = [];

    public DockSplitView? View { get; set; }

    public override UIElement? Element => View;
}

/// <summary>Items as tabs, the selected one shown.</summary>
internal sealed class DockGroup : DockNode
{
    public List<DockItem> Items { get; } = [];

    public DockItem? Selected { get; set; }

    /// <summary>The floating window the group is in; null — it is in the layout.</summary>
    public DockFloat? Float { get; set; }

    /// <summary>The group the items of this one went on to after it left the layout:
    /// the next of them to come back joins them there instead of making a group of its own.</summary>
    public DockGroup? Successor { get; set; }

    public DockGroupView? View { get; set; }

    public override UIElement? Element => View;
}

/// <summary>A floating window: one group, at a place of its own over the layout.</summary>
internal sealed class DockFloat(DockGroup group, Rectangle bounds)
{
    public DockGroup Group { get; } = group;

    /// <summary>In the host's coordinates.</summary>
    public Rectangle Bounds { get; set; } = bounds;

    public DockFloatingWindow? View { get; set; }
}

/// <summary>The tree's operations: no events, no views — the host does both after them.</summary>
internal static class DockTree
{
    /// <summary>The smallest share a node gets when it docks beside another.</summary>
    private const float MinShare = 0.05f;

    public static IEnumerable<DockGroup> Groups(DockNode? node)
    {
        if (node is null) yield break;

        Stack<DockNode> pending = new();
        pending.Push(node);

        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case DockGroup group:
                    yield return group;
                    break;

                case DockSplit split:
                    for (int i = split.Children.Count - 1; i >= 0; i--)
                        pending.Push(split.Children[i]);
                    break;
            }
        }
    }

    public static bool Contains(DockNode? root, DockNode node)
    {
        if (root is null) return false;

        DockNode? current = node;

        while (current?.Parent is { } parent)
            current = parent;

        return ReferenceEquals(current, root);
    }

    /// <summary>Put <paramref name="node"/> beside <paramref name="target"/>, on
    /// <paramref name="side"/>, with <paramref name="share"/> of the target's extent — of
    /// the whole layout's when the target is the root. Returns the new root.</summary>
    public static DockNode InsertBeside(DockNode root, DockNode target, DockNode node, DockPosition side, float share)
    {
        Orientation orientation = side is DockPosition.Left or DockPosition.Right ? Orientation.Horizontal : Orientation.Vertical;
        bool before = side is DockPosition.Left or DockPosition.Top;

        share = Math.Clamp(share, MinShare, 1f - MinShare);

        if (target.Parent is { } parent && parent.Orientation == orientation)
        {
            int at = parent.Children.IndexOf(target);

            node.Weight = target.Weight * share;
            target.Weight *= 1f - share;

            parent.Children.Insert(before ? at : at + 1, node);
            node.Parent = parent;

            return root;
        }

        if (ReferenceEquals(target, root) && root is DockSplit same && same.Orientation == orientation)
        {
            // the whole layout's edge: the node takes its share of all of it, the
            // others keep theirs among themselves
            float total = 0f;
            foreach (DockNode child in same.Children) total += child.Weight;

            node.Weight = total * share / (1f - share);

            same.Children.Insert(before ? 0 : same.Children.Count, node);
            node.Parent = same;

            return root;
        }

        var split = new DockSplit(orientation) { Weight = target.Weight };
        DockNode newRoot = Replace(root, target, split);

        node.Weight = share;
        target.Weight = 1f - share;

        split.Children.Add(before ? node : target);
        split.Children.Add(before ? target : node);
        node.Parent = split;
        target.Parent = split;

        return newRoot;
    }

    /// <summary>Take a group out of the tree; the splits left with one child give their
    /// place to it. Returns the new root — null when the group was all there was.</summary>
    public static DockNode? Remove(DockNode root, DockGroup group)
    {
        if (ReferenceEquals(root, group))
        {
            group.Parent = null;
            return null;
        }

        if (group.Parent is not { } parent) return root;

        parent.Children.Remove(group);
        group.Parent = null;

        return Collapse(root, parent);
    }

    /// <summary>A split with one child gives its place to the child; a child split of
    /// the same orientation as its parent's merges into it.</summary>
    private static DockNode? Collapse(DockNode root, DockSplit split)
    {
        if (split.Children.Count == 0)
        {
            if (ReferenceEquals(split, root)) return null;

            DockSplit? grand = split.Parent;
            grand?.Children.Remove(split);
            split.Parent = null;

            return grand is null ? root : Collapse(root, grand);
        }

        if (split.Children.Count > 1) return root;

        DockNode only = split.Children[0];
        split.Children.Clear();

        only.Weight = split.Weight;

        DockNode newRoot = Replace(root, split, only);

        if (only is DockSplit inner && inner.Parent is { } outer && outer.Orientation == inner.Orientation)
            Merge(outer, inner);

        return newRoot;
    }

    /// <summary>The children of <paramref name="inner"/> in its place in <paramref name="outer"/>,
    /// sharing its weight as they shared it.</summary>
    private static void Merge(DockSplit outer, DockSplit inner)
    {
        int at = outer.Children.IndexOf(inner);
        float total = 0f;

        foreach (DockNode child in inner.Children) total += child.Weight;

        outer.Children.RemoveAt(at);

        foreach (DockNode child in inner.Children)
        {
            child.Weight = total > 0 ? child.Weight / total * inner.Weight : inner.Weight / inner.Children.Count;
            child.Parent = outer;
            outer.Children.Insert(at++, child);
        }

        inner.Children.Clear();
        inner.Parent = null;
    }

    /// <summary><paramref name="replacement"/> where <paramref name="node"/> was. Returns the new root.</summary>
    private static DockNode Replace(DockNode root, DockNode node, DockNode replacement)
    {
        DockSplit? parent = node.Parent;

        replacement.Parent = parent;
        node.Parent = null;

        if (parent is null) return replacement;

        parent.Children[parent.Children.IndexOf(node)] = replacement;

        return ReferenceEquals(root, node) ? replacement : root;
    }

    /// <summary>The tree as it is read back from a layout: splits without children
    /// dropped, splits with one child replaced by it.</summary>
    public static DockNode? Normalize(DockNode? node)
    {
        switch (node)
        {
            case DockGroup group:
                return group.Items.Count > 0 ? group : null;

            case DockSplit split:
                var children = new List<DockNode>(split.Children.Count);

                foreach (DockNode child in split.Children)
                {
                    DockNode? kept = Normalize(child);

                    if (kept is DockSplit inner && inner.Orientation == split.Orientation)
                    {
                        float total = 0f;
                        foreach (DockNode grandChild in inner.Children) total += grandChild.Weight;

                        foreach (DockNode grandChild in inner.Children)
                        {
                            grandChild.Weight = total > 0 ? grandChild.Weight / total * inner.Weight : inner.Weight;
                            children.Add(grandChild);
                        }
                    }
                    else if (kept is not null)
                    {
                        children.Add(kept);
                    }
                }

                split.Children.Clear();

                if (children.Count == 0) return null;

                if (children.Count == 1)
                {
                    children[0].Weight = split.Weight;
                    children[0].Parent = null;
                    return children[0];
                }

                foreach (DockNode child in children)
                {
                    child.Parent = split;
                    split.Children.Add(child);
                }

                return split;

            default:
                return null;
        }
    }
}
