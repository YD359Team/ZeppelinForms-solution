namespace ZeppelinForms.Forms.Controls.Tree;

/// <summary>A tree made from data: the nodes follow the items, see <see cref="TreeItemsSource"/>.</summary>
public partial class TreeView
{
    private TreeItemsSource? _itemsSource;

    /// <summary>The data the tree is made from: the view model's items and their children,
    /// made by <see cref="TreeItemsSource.Create"/>. Null — the nodes are what
    /// <see cref="Nodes"/> holds.</summary>
    /// <remarks>
    /// <para>Each node's <see cref="TreeNode.Content"/> is its item — <see cref="ItemText"/>
    /// and <see cref="ItemIcon"/> read it, <c>SelectedNode?.Content</c> is the selected
    /// item. An item's children become nodes when its node is expanded the first time.</para>
    /// <para>The collections that tell of their changes, and the items that do, are
    /// followed while the tree is in a form: an item added shows, a property changed is
    /// drawn again, and the expansion and the check boxes go both ways when the source
    /// has them (<see cref="TreeItemsSource{T}.WithExpanded"/>,
    /// <see cref="TreeItemsSource{T}.WithChecked"/>). Out of the form the tree holds on to
    /// nothing of the data, and is put in step with it when it comes back.</para>
    /// <para>The nodes are the source's: change the data, not <see cref="Nodes"/>. One
    /// source makes one tree.</para>
    /// </remarks>
    public TreeItemsSource? ItemsSource
    {
        get => _itemsSource;
        set
        {
            if (ReferenceEquals(_itemsSource, value)) return;

            _itemsSource?.Detach();

            // the nodes of the previous source, or the ones put in by hand, go
            _root.Children.Clear();

            _itemsSource = value;

            if (value is null) return;

            if (FindOwner() is not null) value.Attach(this);
            else value.Fill(this);
        }
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        // the data is listened to only while the tree is in a form
        _itemsSource?.Attach(this);
    }

    protected override void OnDetached()
    {
        _itemsSource?.Detach();

        base.OnDetached();
    }
}
