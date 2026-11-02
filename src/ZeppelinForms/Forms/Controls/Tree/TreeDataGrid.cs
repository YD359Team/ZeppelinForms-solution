using ZeppelinForms.Data;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Controls.Tools;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms.Controls.Tree;

/// <summary>
/// A tree with columns: the rows of a <see cref="DataGridView"/> are the expanded nodes of
/// a tree, and the tree's column — the first by default — indents its cells by depth and
/// gives them an expander.
/// </summary>
/// <remarks>
/// <para>Everything of the grid stays: the column types, editing, selection by rows or
/// cells, resizing, virtualization. A row's record — what the columns read — is its
/// node's <see cref="TreeNode.Content"/>, so a typed column reads the items of
/// <see cref="ItemsSource"/> as they are.</para>
/// <para>A sort orders the children of each node, not the rows: a child stays under its
/// parent. <see cref="DataGridView.Items"/> is the tree's: the visible rows' records,
/// changed only by the nodes; a <see cref="DataGridView.DataSource"/> is not taken.</para>
/// </remarks>
/// <example>
/// <code>
/// var grid = new TreeDataGrid
/// {
///     Columns =
///     {
///         new DataGridViewColumn&lt;Entry, string&gt;(e =&gt; e.Name),
///         new DataGridViewColumn&lt;Entry, long&gt;(e =&gt; e.Size),
///     },
///     ItemsSource = TreeItemsSource.Create(entries, e =&gt; e.Children),
/// };
/// </code>
/// </example>
public partial class TreeDataGrid : DataGridView, ITreeHost
{
    private const float ExpanderWidth = 18f;

    /// <summary>The service root, as the tree view has one: one subscription to the
    /// changes of all the nodes.</summary>
    private readonly TreeNode _root = new();

    /// <summary>The nodes of the rows, in the order they are on screen.</summary>
    private readonly List<TreeNode> _rows = [];

    private int _updateDepth;
    private bool _rebuildPending;

    /// <summary>The rows are being written: the grid asking for the order back is the
    /// tree's own write coming round, not a sort.</summary>
    private bool _flattening;

    private TreeItemsSource? _itemsSource;

    public TreeDataGrid()
    {
        _root.Changed += OnTreeChanged;
        _root.Expanding += OnNodeExpanding;

        // the records are the tree's: a write from outside throws, as with a data source
        ((ItemCollection)Items).IsBound = true;
    }

    /// <summary>The top-level nodes. With <see cref="ItemsSource"/> set, the source's.</summary>
    public IList<TreeNode> Nodes => _root.Children;

    /// <summary>The data the tree is made from, as <see cref="TreeView.ItemsSource"/> is.</summary>
    public TreeItemsSource? ItemsSource
    {
        get => _itemsSource;
        set
        {
            if (ReferenceEquals(_itemsSource, value)) return;

            _itemsSource?.Detach();
            _root.Children.Clear();
            _itemsSource = value;

            if (value is null) return;

            if (FindOwner() is not null) value.Attach(this);
            else value.Fill(this);
        }
    }

    /// <summary>The column whose cells are indented and have the expander. −1 — none: the
    /// rows are then a flat list of the expanded nodes.</summary>
    public int TreeColumnIndex
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    }

    /// <summary>The indent per level of depth.</summary>
    public float Indent
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = 16f;

    /// <summary>The expander's chevron.</summary>
    [Styled(Category = "Tree")]
    public partial Color GlyphColor { get; set; }
    private static Color GlyphColorDefault => new(255, 90, 90, 90);

    /// <summary>A node is being expanded: the place to make its children, as
    /// <see cref="TreeView.NodeExpanding"/> is.</summary>
    public event EventHandler<TreeNode>? NodeExpanding;

    /// <summary>The node of a row on screen; null — no such row.</summary>
    public TreeNode? NodeAt(int row) => row >= 0 && row < _rows.Count ? _rows[row] : null;

    /// <summary>The row of a node on screen; −1 — it is under a collapsed ancestor or not
    /// in the tree.</summary>
    public int RowOf(TreeNode node) => _rows.IndexOf(node);

    /// <summary>The lead row's node. Setting it expands its ancestors, selects its row and
    /// brings it into sight.</summary>
    public TreeNode? SelectedNode
    {
        get => NodeAt(SelectedIndex);
        set
        {
            if (value is null)
            {
                SelectedIndex = -1;
                return;
            }

            value.ExpandAncestors();

            int row = RowOf(value);

            SelectedIndex = row;

            if (row >= 0) ScrollIntoView(row);
        }
    }

    public void ExpandAll() => SetExpandedAll(true);

    public void CollapseAll() => SetExpandedAll(false);

    private void SetExpandedAll(bool expanded)
    {
        BeginUpdate();

        try
        {
            Stack<TreeNode> pending = new(_root.Children);

            while (pending.Count > 0)
            {
                TreeNode node = pending.Pop();

                if (node.HasChildren) node.IsExpanded = expanded;

                foreach (TreeNode child in node.Children)
                    pending.Push(child);
            }
        }
        finally
        {
            EndUpdate();
        }
    }

    /// <summary>Stop making the rows again on every change until <see cref="EndUpdate"/>.
    /// The calls nest.</summary>
    public void BeginUpdate() => _updateDepth++;

    public void EndUpdate()
    {
        if (_updateDepth == 0) return;

        if (--_updateDepth > 0 || !_rebuildPending) return;

        _rebuildPending = false;
        Flatten();
    }

    // ===== the rows from the nodes =====

    private void OnTreeChanged(TreeNode node, TreeChangeKind kind)
    {
        if (kind == TreeChangeKind.Content)
        {
            InvalidateVisual();
            return;
        }

        if (_updateDepth > 0)
        {
            _rebuildPending = true;
            return;
        }

        Flatten();
    }

    private void OnNodeExpanding(TreeNode node)
    {
        if (NodeExpanding is null && _itemsSource is null) return;

        BeginUpdate();

        try
        {
            _itemsSource?.Realize(node);
            NodeExpanding?.Invoke(this, node);
        }
        finally
        {
            EndUpdate();
        }
    }

    /// <summary>The expanded nodes into rows, each level in the sort's order, and their
    /// records into the grid as one change: the grid keeps the selection by record.</summary>
    private void Flatten()
    {
        _rows.Clear();

        Comparison<TreeNode>? order = SiblingOrder();
        Stack<TreeNode> pending = new();

        PushChildren(_root, pending, order);

        while (pending.Count > 0)
        {
            TreeNode node = pending.Pop();
            _rows.Add(node);

            if (node.IsExpanded) PushChildren(node, pending, order);
        }

        object[] records = new object[_rows.Count];

        for (int i = 0; i < records.Length; i++)
            records[i] = _rows[i].Content ?? _rows[i];

        _flattening = true;

        try
        {
            ((IItemsSink)Items).ResetTo(records);
        }
        finally
        {
            _flattening = false;
        }
    }

    /// <summary>A node's children onto the walk's stack: backwards, so they come off it in order.</summary>
    private static void PushChildren(TreeNode node, Stack<TreeNode> pending, Comparison<TreeNode>? order)
    {
        if (order is null)
        {
            for (int i = node.Children.Count - 1; i >= 0; i--)
                pending.Push(node.Children[i]);

            return;
        }

        // stable: siblings the column calls equal keep their own order
        TreeNode[] sorted = [.. node.Children
            .Select((child, index) => (child, index))
            .OrderBy(pair => pair.child, Comparer<TreeNode>.Create(order))
            .ThenBy(pair => pair.index)
            .Select(pair => pair.child)];

        for (int i = sorted.Length - 1; i >= 0; i--)
            pending.Push(sorted[i]);
    }

    /// <summary>The order of siblings by the sort column; null — the nodes' own.</summary>
    private Comparison<TreeNode>? SiblingOrder()
    {
        if (SortColumnIndex < 0 || SortColumnIndex >= Columns.Count) return null;

        DataGridViewColumn column = Columns[SortColumnIndex];
        bool descending = SortDescending;

        return (left, right) =>
        {
            int result = column.CompareRows(left.Content ?? left, right.Content ?? right);

            return descending ? -result : result;
        };
    }

    private protected override bool SortsByOrder => false;

    /// <summary>The sort changed — or the rows did: the rows are made again in the new
    /// order, unless this is the tree's own write coming round.</summary>
    private protected override void OnSortOrderChanged()
    {
        if (_flattening) return;

        Flatten();
    }

    private protected override bool AcceptsDataSource => false;

    // ===== the tree's column =====

    private float IndentOf(TreeNode node) => Math.Max(0, node.Level - 1) * Indent;

    private protected override float CellIndent(int row, int column) =>
        column == TreeColumnIndex && NodeAt(row) is { } node ? IndentOf(node) + ExpanderWidth : 0f;

    private protected override void DrawCellAdornment(Graphics g, int row, int column, Rectangle cell)
    {
        if (NodeAt(row) is not { HasChildren: true } node) return;

        Glyphs.DrawChevron(
            g,
            new Point(cell.X + IndentOf(node) + ExpanderWidth / 2f, cell.Y + cell.Height / 2f),
            4f,
            node.IsExpanded,
            GlyphColor);
    }

    private protected override bool OnCellAdornmentClick(int row, int column, float x)
    {
        if (NodeAt(row) is not { HasChildren: true } node) return false;

        float start = IndentOf(node);

        // the indent itself is the row's: a click there selects
        if (x < start || x >= start + ExpanderWidth) return false;

        node.IsExpanded = !node.IsExpanded;
        return true;
    }

    // ===== keyboard =====

    /// <summary>Right expands the lead row's node, or goes to its first child; Left
    /// collapses it, or goes to its parent — on rows, or in the tree's column when
    /// selecting cells. The other keys are the grid's.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool onTree = SelectionUnit == DataGridViewSelectionUnit.FullRow || CurrentColumnIndex == TreeColumnIndex;

        if (onTree && e.Modifiers == KeyModifiers.None && SelectedNode is { } node)
        {
            switch (e.Key)
            {
                case Key.Right when node.HasChildren && !node.IsExpanded:
                    node.Expand();
                    e.Handled = true;
                    return;

                case Key.Right when node.IsExpanded && node.Children.Count > 0:
                    SelectedNode = node.Children[0];
                    e.Handled = true;
                    return;

                case Key.Left when node.IsExpanded && node.HasChildren:
                    node.Collapse();
                    e.Handled = true;
                    return;

                case Key.Left when node.Parent is { } parent && !ReferenceEquals(parent, _root):
                    SelectedNode = parent;
                    e.Handled = true;
                    return;
            }
        }

        base.OnKeyDown(e);
    }

    // ===== the source's life =====

    protected override void OnAttached()
    {
        base.OnAttached();

        _itemsSource?.Attach(this);
    }

    protected override void OnDetached()
    {
        _itemsSource?.Detach();

        base.OnDetached();
    }
}
