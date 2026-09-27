using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms.Controls.Tree;

/// <summary>A tree with virtualization.</summary>
/// <remarks>
/// Expanded nodes are unrolled into a flat list, which is given to
/// VirtualizingStackPanel. Nested containers would be simpler but would kill
/// virtualization: the visible range there is computed by dividing the scroll
/// by the row height, and that requires a one-dimensional sequence.
///
/// The panel is nested rather than inherited: its ItemsSource and ItemTemplate
/// are public, while for the tree they are internal — any write from outside
/// would drift apart from the projection.
/// </remarks>
public class TreeView : DecoratedPanel, IInputElement
{
    private readonly VirtualizingStackPanel _panel = new();
    private readonly List<object> _flat = [];
    private readonly TreeNode _root = new();

    /// <summary>The root nodes. The service root is not visible from outside:
    /// it is needed so that there is one subscription to changes rather than
    /// one per node.</summary>
    public IList<TreeNode> Nodes => _root.Children;

    /// <summary>The indent per nesting level.</summary>
    public float Indent
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the row height is fixed, so the indent is only drawing
            InvalidateVisual();
        }
    } = 16f;

    public float ItemHeight
    {
        get => _panel.ItemHeight;
        set => _panel.ItemHeight = value;
    }

    /// <summary>How to get a node's caption. By default — ToString of the content.</summary>
    public Func<TreeNode, string>? ItemText
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the captions of already created rows are stale
            InvalidateVisual();
        }
    }

    public event EventHandler<TreeNode?>? SelectionChanged;

    // the tree takes focus: without it there is no keyboard
    public bool IsFocused { get; set; }

    public bool TabStop { get; set; } = true;

    public uint TabIndex { get; set; }

    public TreeNode? SelectedNode
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;

            SelectionChanged?.Invoke(this, value);

            // a redraw rather than a rebuild: the set of visible rows is the same,
            // only the fill of two of them changed
            InvalidateVisual();
        }
    }

    public TreeView()
    {
        _panel.ItemsSource = _flat;
        _panel.ItemTemplate = CreateRow;

        // without this a tree taller than its box is clipped, and virtualization
        // computes the range from a forever-zero ScrollY
        _panel.OverflowY = Overflow.Auto;

        Children.Add(_panel);

        _root.Changed += OnTreeChanged;

        // the tree is a container, not an in-place control: there is no point
        // centering it, and the Center inherited from UnitControl does exactly that
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);

        Rebuild();
    }

    internal string GetItemText(TreeNode node) =>
        ItemText?.Invoke(node) ?? node.Content?.ToString() ?? string.Empty;

    public void ExpandAll() => SetExpandedAll(true);

    public void CollapseAll() => SetExpandedAll(false);

    private void SetExpandedAll(bool expanded)
    {
        Stack<TreeNode> pending = new();

        foreach (TreeNode node in _root.Children)
            pending.Push(node);

        while (pending.Count > 0)
        {
            TreeNode node = pending.Pop();

            if (node.HasChildren)
                node.IsExpanded = expanded;

            foreach (TreeNode child in node.Children)
                pending.Push(child);
        }
    }

    private void OnTreeChanged(TreeNode node, TreeChangeKind kind)
    {
        if (kind == TreeChangeKind.Content)
        {
            InvalidateVisual();
            return;
        }

        Rebuild();
    }

    private void Rebuild()
    {
        TreeFlattener.Flatten(_root.Children, _flat);

        // Refresh is mandatory, not only Invalidate: UpdateRealizedRange exits early
        // when first and count haven't changed, and after a node is expanded the range
        // is often the same — the contents of the list change entirely, and without
        // the reset the rows would stay from the old projection.
        // The reset doesn't recreate rows: the panel matches containers by node,
        // so the already visible rows stay the same objects and only move,
        // and only the rows of new children are created
        _panel.Refresh();

        // the selected node may have gone away together with a collapsed ancestor
        if (SelectedNode is not null && !_flat.Contains(SelectedNode))
            SelectedNode = null;
    }

    private UIElement CreateRow(object item) => new TreeViewItem(this, (TreeNode)item);

    // ===== keyboard =====

    /// <summary>The node's place in the unrolled list, or −1 if it is under
    /// a collapsed ancestor and not shown right now.</summary>
    private int RowOf(TreeNode? node) => node is null ? -1 : _flat.IndexOf(node);

    /// <summary>How many rows fit in the window — that is how far
    /// PageUp and PageDown move.</summary>
    private int PageRows =>
        ItemHeight <= 0 ? 1 : Math.Max(1, (int)(_panel.ActualSize.Height / ItemHeight) - 1);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_flat.Count == 0) return;

        TreeNode? current = SelectedNode;
        int row = RowOf(current);

        switch (e.Key)
        {
            case Key.Down:
                SelectRow(row + 1);
                break;

            case Key.Up:
                // focus without a selection — the first arrow picks the edge
                // rather than jumping into nowhere
                SelectRow(row < 0 ? _flat.Count - 1 : row - 1);
                break;

            case Key.Home:
                SelectRow(0);
                break;

            case Key.End:
                SelectRow(_flat.Count - 1);
                break;

            case Key.PageDown:
                SelectRow(row < 0 ? 0 : row + PageRows);
                break;

            case Key.PageUp:
                SelectRow(row < 0 ? 0 : row - PageRows);
                break;

            case Key.Right:
                // a closed node expands, an expanded one lets you inside
                if (current is null) SelectRow(0);
                else if (current.HasChildren && !current.IsExpanded) current.Expand();
                else if (current.HasChildren) SelectRow(row + 1);
                else return;

                break;

            case Key.Left:
                // an expanded one collapses, a collapsed one hands the focus to its parent
                if (current is null) SelectRow(0);
                else if (current.IsExpanded && current.HasChildren) current.Collapse();
                else if (current.Parent is { } parent && RowOf(parent) >= 0) SelectNode(parent);
                else return;

                break;

            case Key.Enter:
            case Key.Space:
                if (current is not { HasChildren: true }) return;

                current.IsExpanded = !current.IsExpanded;
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private void SelectRow(int row)
    {
        if (_flat.Count == 0) return;

        SelectNode((TreeNode)_flat[Math.Clamp(row, 0, _flat.Count - 1)]);
    }

    private void SelectNode(TreeNode node)
    {
        SelectedNode = node;

        ScrollIntoView(node);
    }

    /// <summary>Bring a node into the visible part. A node under a collapsed
    /// ancestor is not shown at all — there is nowhere to bring it.</summary>
    public void ScrollIntoView(TreeNode node)
    {
        int row = RowOf(node);
        if (row < 0 || ItemHeight <= 0) return;

        float top = row * ItemHeight;
        float bottom = top + ItemHeight;
        float viewport = _panel.ActualSize.Height;

        if (top < _panel.ScrollY)
            _panel.ScrollTo(_panel.ScrollX, top);
        else if (bottom > _panel.ScrollY + viewport)
            _panel.ScrollTo(_panel.ScrollX, bottom - viewport);
    }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        _panel.Measure(inner);

        var content = new Size(
            _panel.DesiredSize.Width + Padding.Horizontal,
            _panel.DesiredSize.Height + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }

    protected override void ArrangeContentOverride(Size finalSize)
    {
        _panel.Arrange(new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical))));
    }
}