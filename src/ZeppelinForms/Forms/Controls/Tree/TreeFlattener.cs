namespace ZeppelinForms.Forms.Controls.Tree;

/// <summary>Unrolls the expanded nodes into a flat list — in the order
/// they are seen on screen.</summary>
/// <remarks>
/// The flat list is needed by virtualization: VirtualizingStackPanel computes
/// the visible range by dividing the scroll by the row height, and for that
/// the rows must be a one-dimensional sequence. Nested containers kill
/// virtualization.
/// </remarks>
internal static class TreeFlattener
{
    /// <summary>The walk is iterative rather than recursive: the tree depth is set
    /// by the data, not by us, and a chain of tens of thousands of nodes would
    /// overflow the stack right in the middle of layout.</summary>
    public static void Flatten(IReadOnlyList<TreeNode> roots, List<object> output)
    {
        output.Clear();

        if (roots.Count == 0) return;

        Stack<TreeNode> pending = new();

        for (int i = roots.Count - 1; i >= 0; i--)
            pending.Push(roots[i]);

        while (pending.Count > 0)
        {
            TreeNode node = pending.Pop();

            output.Add(node);

            if (!node.IsExpanded) continue;

            // in reverse order: the stack will return them in forward order
            for (int i = node.Children.Count - 1; i >= 0; i--)
                pending.Push(node.Children[i]);
        }
    }
}