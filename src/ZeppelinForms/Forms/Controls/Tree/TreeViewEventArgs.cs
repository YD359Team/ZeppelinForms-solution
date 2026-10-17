namespace ZeppelinForms.Forms.Controls.Tree;

/// <summary>A node was invoked: double-clicked, or Enter pressed on it.</summary>
public sealed class TreeNodeInvokedEventArgs(TreeNode node) : EventArgs
{
    public TreeNode Node { get; } = node;

    /// <summary>The handler did what the node is for — opened it, showed it. Left
    /// false, a node with children is expanded or collapsed, as it is without a handler.</summary>
    public bool Handled { get; set; }
}

/// <summary>Editing a node's caption is about to begin.</summary>
public sealed class TreeNodeEditingEventArgs(TreeNode node, string text) : EventArgs
{
    public TreeNode Node { get; } = node;

    /// <summary>What the editor starts with: the caption, or the name without what
    /// the caption adds to it — "Bone_Head" for "Bone_Head (12 vertices)".</summary>
    public string Text { get; set; } = text;

    /// <summary>The node can't be renamed: the editor doesn't open.</summary>
    public bool Cancel { get; set; }
}

/// <summary>Editing a node's caption ended with a new text.</summary>
/// <remarks>Not raised when the edit is cancelled — Escape, or a node removed meanwhile.</remarks>
public sealed class TreeNodeEditedEventArgs(TreeNode node, string text) : EventArgs
{
    public TreeNode Node { get; } = node;

    /// <summary>The text typed. A handler may change it — trim it, make it unique —
    /// and the changed text is what a string content becomes.</summary>
    public string Text { get; set; } = text;

    /// <summary>Reject the text: the node keeps its caption. A node whose
    /// <see cref="TreeNode.Content"/> is a string or null takes <see cref="Text"/>
    /// unless rejected; any other content is the handler's to change.</summary>
    public bool Cancel { get; set; }
}