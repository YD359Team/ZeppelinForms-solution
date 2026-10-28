using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Markup;
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

    /// <summary>The selected nodes, the lead among them. A set of nodes rather than of
    /// rows: a row number changes with every expand above it, a node stays itself.</summary>
    private readonly HashSet<TreeNode> _selected = [];

    /// <summary>Where a Shift range starts: the node clicked or reached last without Shift.</summary>
    private TreeNode? _anchor;

    /// <summary>The caption editor while a node is renamed; null — none is.</summary>
    private TextBox? _editor;

    /// <summary>The node being renamed.</summary>
    private TreeNode? _editing;

    /// <summary>How deep <see cref="BeginUpdate"/> calls are nested; above zero the
    /// rows are not rebuilt on every change.</summary>
    private int _updateDepth;

    /// <summary>A change came while updating: the rows are rebuilt at the last
    /// <see cref="EndUpdate"/>.</summary>
    private bool _rebuildPending;

    /// <summary>A node to bring into sight on the next layout: right after its ancestors
    /// expand, the rows under them are not measured yet, and a scroll clamped to the
    /// old, shorter extent stops short of it.</summary>
    private TreeNode? _reveal;

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

    /// <summary>The rows scroll in the tree's own panel: the setting goes there.</summary>
    protected override void OnScrollPastEndChanged() => _panel.ScrollPastEnd = ScrollPastEnd;

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

    /// <summary>The icon of a node without one of its own — by the kind of its content:
    /// a bone, a helper, an attachment. Null — only the nodes' own icons.</summary>
    public Func<TreeNode, IconSource?>? ItemIcon
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // a column for the icons appears or goes: the rows are measured again
            Rebuild();
        }
    }

    /// <summary>A check box before every node: <see cref="TreeNode.IsChecked"/>.
    /// A click on it, or Space, checks and unchecks; Space does it for all the
    /// selected nodes at once.</summary>
    public bool CheckBoxes
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Rebuild();
        }
    }

    /// <summary>How many nodes can be selected, and how: see <see cref="Enums.SelectionMode"/>.</summary>
    public SelectionMode SelectionMode
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // down to one: the lead stays
            if (value == SelectionMode.Single && _selected.Count > 1)
                SelectOnly(SelectedNode);
        }
    }

    /// <summary>The lead node changed, or the set of the selected ones did. The argument
    /// is the lead — <see cref="SelectedNode"/>.</summary>
    public event EventHandler<TreeNode?>? SelectionChanged;

    /// <summary>A node was checked or unchecked — from its check box, Space or code.</summary>
    public event EventHandler<TreeNode>? NodeCheckedChanged;

    /// <summary>A node was double-clicked, or Enter was pressed on it.</summary>
    public event EventHandler<TreeNodeInvokedEventArgs>? NodeInvoked;

    /// <summary>A node is being expanded: the place to make its children, when the
    /// node has <see cref="TreeNode.HasUnrealizedChildren"/>. The children added here
    /// are shown by the same rebuild the expansion makes; ones that come later — read
    /// on another thread — are added on the UI thread, in <see cref="BeginUpdate"/>.</summary>
    public event EventHandler<TreeNode>? NodeExpanding;

    /// <summary>Stop rebuilding the rows on every change until <see cref="EndUpdate"/>:
    /// a thousand children added one by one would rebuild the projection a thousand
    /// times. The calls nest.</summary>
    public void BeginUpdate() => _updateDepth++;

    /// <summary>Close a <see cref="BeginUpdate"/>; the last one rebuilds the rows if
    /// anything changed in between.</summary>
    public void EndUpdate()
    {
        if (_updateDepth == 0) return;

        if (--_updateDepth > 0 || !_rebuildPending) return;

        _rebuildPending = false;
        Rebuild();
    }

    private void OnNodeExpanding(TreeNode node)
    {
        if (NodeExpanding is null) return;

        BeginUpdate();

        try
        {
            NodeExpanding.Invoke(this, node);
        }
        finally
        {
            EndUpdate();
        }
    }

    // the tree takes focus: without it there is no keyboard
    public bool IsFocused { get; set; }

    public bool TabStop { get; set; } = true;

    public uint TabIndex { get; set; }

    /// <summary>The lead of the selection: the node selected last, where the keyboard is.</summary>
    private TreeNode? _lead;

    /// <summary>The lead node: the one selected last, where the keyboard is. Setting it
    /// selects that node alone and scrolls it into sight, as a selection from the
    /// keyboard does; null clears the selection.</summary>
    [SetAfterContent]
    public TreeNode? SelectedNode
    {
        get => _lead;
        set
        {
            SelectOnly(value);

            if (value is not null)
                ScrollIntoView(value);
        }
    }

    /// <summary>The selected nodes, top to bottom.</summary>
    public IReadOnlyList<TreeNode> SelectedNodes =>
        _selected.Count == 0 ? [] : [.. _flat.Cast<TreeNode>().Where(_selected.Contains)];

    public TreeView()
    {
        _panel.ItemsSource = _flat;
        _panel.ItemTemplate = CreateRow;

        // without this a tree taller than its box is clipped, and virtualization
        // computes the range from a forever-zero ScrollY
        _panel.OverflowY = Overflow.Auto;

        Children.Add(_panel);

        _root.Changed += OnTreeChanged;
        _root.CheckedChanged += node => NodeCheckedChanged?.Invoke(this, node);
        _root.Expanding += OnNodeExpanding;

        // the tree is a container, not an in-place control: there is no point
        // centering it, and the Center inherited from UnitControl does exactly that
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);

        Rebuild();
    }

    internal string GetItemText(TreeNode node) =>
        ItemText?.Invoke(node) ?? node.Content?.ToString() ?? string.Empty;

    internal IconSource? GetItemIcon(TreeNode node) => node.Icon ?? ItemIcon?.Invoke(node);

    /// <summary>Whether any node has an icon of its own; null — not known since the
    /// last change. Kept rather than asked per row: every row asks as it draws.</summary>
    private bool? _nodesHaveIcons;

    /// <summary>Whether any icon can show: the rows keep a column for icons then, so
    /// the captions of nodes with and without one line up.</summary>
    internal bool ShowsIcons => ItemIcon is not null || (_nodesHaveIcons ??= HasIconIn(_root));

    private static bool HasIconIn(TreeNode node)
    {
        foreach (TreeNode child in node.Children)
        {
            if (child.Icon is not null || HasIconIn(child))
                return true;
        }

        return false;
    }

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
        if (_updateDepth > 0)
        {
            _nodesHaveIcons = null;
            _rebuildPending = true;
            return;
        }

        bool icons = ShowsIcons;
        _nodesHaveIcons = null;

        // the first icon, or the last one gone: the column comes or goes, and the
        // rows are measured again — a redraw would keep their old widths
        if (kind == TreeChangeKind.Content && ShowsIcons == icons)
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

        DropHiddenSelection();

        // the node being renamed went away with a collapsed ancestor or was removed:
        // there is nothing left to rename
        if (_editing is not null && !_flat.Contains(_editing))
            EndEdit(cancel: true);
    }

    /// <summary>The selected nodes may have gone away together with a collapsed
    /// ancestor, or out of the tree: they are deselected.</summary>
    private void DropHiddenSelection()
    {
        if (_selected.Count == 0 && _anchor is null) return;

        var shown = new HashSet<object>(_flat);

        int removed = _selected.RemoveWhere(node => !shown.Contains(node));

        if (_anchor is not null && !shown.Contains(_anchor)) _anchor = null;

        bool leadGone = SelectedNode is not null && !shown.Contains(SelectedNode);

        if (leadGone)
        {
            // the last of the selection that is still seen leads, or nothing does
            _lead = _selected.Count == 0
                ? null
                : _flat.Cast<TreeNode>().Last(_selected.Contains);
        }

        if (removed > 0 || leadGone)
            RaiseSelectionChanged();
    }

    private TreeViewItem CreateRow(object item) => new(this, (TreeNode)item);

    // ===== selection =====

    public bool IsSelected(TreeNode node) => _selected.Contains(node);

    /// <summary>Select or deselect a node without touching the others — in
    /// <see cref="SelectionMode.Single"/> selecting one deselects the rest. A node
    /// under a collapsed ancestor is not shown and can't be selected.</summary>
    public void SetSelected(TreeNode node, bool selected)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (selected && SelectionMode == SelectionMode.Single)
        {
            SelectOnly(node);
            return;
        }

        if (selected)
        {
            if (!_flat.Contains(node) || !_selected.Add(node)) return;

            _lead = node;
            _anchor = node;
        }
        else
        {
            if (!_selected.Remove(node)) return;

            if (ReferenceEquals(SelectedNode, node))
                _lead = _selected.Count == 0 ? null : _flat.Cast<TreeNode>().Last(_selected.Contains);
        }

        RaiseSelectionChanged();
    }

    public void ClearSelection() => SelectOnly(null);

    /// <summary>Select every node that is shown. Not in <see cref="SelectionMode.Single"/>.</summary>
    public void SelectAll()
    {
        if (SelectionMode == SelectionMode.Single || _flat.Count == 0) return;

        foreach (TreeNode node in _flat.Cast<TreeNode>())
            _selected.Add(node);

        _lead ??= (TreeNode)_flat[0];
        _anchor ??= _lead;

        RaiseSelectionChanged();
    }

    /// <summary>The node alone, the rest deselected; null — nothing selected.</summary>
    private void SelectOnly(TreeNode? node)
    {
        if (node is not null && !_flat.Contains(node)) return;

        bool same = ReferenceEquals(SelectedNode, node) &&
                    (node is null ? _selected.Count == 0 : _selected.Count == 1 && _selected.Contains(node));

        _anchor = node;

        if (same) return;

        _selected.Clear();
        if (node is not null) _selected.Add(node);

        _lead = node;

        RaiseSelectionChanged();
    }

    /// <summary>The nodes from the anchor to this one, the rest deselected.</summary>
    private void SelectRange(TreeNode to)
    {
        int from = RowOf(_anchor);
        int last = RowOf(to);

        if (from < 0 || last < 0)
        {
            SelectOnly(to);
            return;
        }

        if (from > last) (from, last) = (last, from);

        _selected.Clear();

        for (int row = from; row <= last; row++)
            _selected.Add((TreeNode)_flat[row]);

        _lead = to;

        RaiseSelectionChanged();
    }

    /// <summary>A press on a node's row: what it selects depends on the mode and the
    /// modifiers, as in Explorer.</summary>
    internal void SelectFromPointer(TreeNode node, KeyModifiers modifiers)
    {
        switch (SelectionMode)
        {
            case SelectionMode.Multiple:
                SetSelected(node, !IsSelected(node));
                break;

            case SelectionMode.Extended when modifiers.HasFlag(KeyModifiers.Shift):
                SelectRange(node);
                break;

            case SelectionMode.Extended when modifiers.HasFlag(KeyModifiers.Control):
                SetSelected(node, !IsSelected(node));
                _anchor = node;
                break;

            default:
                SelectOnly(node);
                break;
        }
    }

    /// <summary>A right press: a node outside the selection becomes the selection, as in
    /// Explorer — the context menu then acts on the node under the cursor. A node
    /// inside a multiple selection keeps it: the menu acts on all of them.</summary>
    internal void SelectForContextMenu(TreeNode node)
    {
        if (!IsSelected(node))
            SelectOnly(node);
    }

    private void RaiseSelectionChanged()
    {
        SelectionChanged?.Invoke(this, SelectedNode);

        // a redraw rather than a rebuild: the set of visible rows is the same,
        // only the fill of some of them changed
        InvalidateVisual();
    }

    // ===== nodes under the pointer =====

    /// <summary>The node whose row is at this point of the form — for a context menu or
    /// a drop on the node under the cursor. Null — no row there.</summary>
    public TreeNode? NodeAt(Point location)
    {
        foreach (UIElement child in _panel.Children)
        {
            if (child is not TreeViewItem { IsVisible: true } row) continue;

            Point at = row.GetAbsolutePosition();

            if (location.X >= at.X && location.X < at.X + row.ActualSize.Width &&
                location.Y >= at.Y && location.Y < at.Y + row.ActualSize.Height)
                return row.Node;
        }

        return null;
    }

    /// <summary>The menu of a node, for a right click on its row: built per node, so it
    /// can say what applies to this one — a bone has other commands than an emitter.
    /// Null, or a null menu — the tree's own <see cref="UIElement.ContextMenu"/>.</summary>
    /// <remarks>The node is selected before the menu is asked for, see
    /// <see cref="SelectedNodes"/> for the menu's commands to act on.</remarks>
    public Func<TreeNode, List<MenuItem>?>? NodeContextMenu { get; set; }

    /// <summary>Invoke a node: <see cref="NodeInvoked"/>, and if no handler took it,
    /// expand or collapse a node with children.</summary>
    internal void Invoke(TreeNode node)
    {
        var args = new TreeNodeInvokedEventArgs(node);
        NodeInvoked?.Invoke(this, args);

        if (!args.Handled && node.HasChildren)
            node.IsExpanded = !node.IsExpanded;
    }

    /// <summary>Check or uncheck the selected nodes together, by the lead's state:
    /// what the lead becomes, all of them become.</summary>
    private void ToggleCheckedSelection()
    {
        if (SelectedNode is not { } lead) return;

        bool check = !lead.IsChecked;

        foreach (TreeNode node in SelectedNodes)
            node.IsChecked = check;

        // the lead even if it isn't in the set — a node can lead an emptied selection
        lead.IsChecked = check;
    }

    // ===== renaming =====

    /// <summary>F2 renames the selected node.</summary>
    public bool LabelEdit { get; set; }

    /// <summary>The node being renamed; null — none is.</summary>
    public TreeNode? EditingNode => _editing;

    /// <summary>Editing a node's caption is about to begin; it can be cancelled, and
    /// the text the editor starts with can be changed.</summary>
    public event EventHandler<TreeNodeEditingEventArgs>? NodeEditing;

    /// <summary>A node's caption was edited: the new text, which can be changed or rejected.</summary>
    public event EventHandler<TreeNodeEditedEventArgs>? NodeEdited;

    /// <summary>Rename a node in place: an editor over its caption, Enter or a click
    /// elsewhere commits, Escape cancels. Works whether or not <see cref="LabelEdit"/>
    /// is on — that one is only about F2.</summary>
    /// <returns>Whether the editor opened: false while the tree is disabled or not in
    /// a form, for a node not in the tree, or when <see cref="NodeEditing"/> cancelled.</returns>
    public bool BeginEdit(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        EndEdit(cancel: false);

        if (!IsEffectivelyEnabled || FindForm() is null || !IsInTree(node)) return false;

        var args = new TreeNodeEditingEventArgs(node, GetItemText(node));
        NodeEditing?.Invoke(this, args);

        if (args.Cancel) return false;

        node.ExpandAncestors();
        SelectOnly(node);
        ScrollIntoView(node);

        _editing = node;

        // stretched over the caption: a text box of a control's default alignment
        // stands centered at its own width in the middle of the row
        _editor = new TextBox
        {
            Text = args.Text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _editor.LostFocus += OnEditorLostFocus;

        Children.Add(_editor);
        Invalidate();

        _editor.SelectAll();
        _editor.Focus();

        return true;
    }

    /// <summary>End renaming: commit the text, or cancel and keep the caption.
    /// Nothing happens when no node is being renamed.</summary>
    public void EndEdit(bool cancel)
    {
        if (_editing is not { } node || _editor is not { } editor) return;

        // cleared first: removing the editor takes its focus, and the lost focus
        // would commit a second time
        _editing = null;
        _editor = null;
        editor.LostFocus -= OnEditorLostFocus;

        string text = editor.Text ?? string.Empty;

        // the focus comes back to the tree before the editor leaves: the keyboard
        // goes on in the tree, as it does after renaming in Explorer
        if (editor.IsFocused) Focus();

        Children.Remove(editor);
        Invalidate();

        if (cancel || !IsInTree(node)) return;

        var args = new TreeNodeEditedEventArgs(node, text);
        NodeEdited?.Invoke(this, args);

        if (!args.Cancel && node.Content is null or string)
            node.Content = args.Text;
    }

    private void OnEditorLostFocus(object? sender, EventArgs e) => EndEdit(cancel: false);

    private bool IsInTree(TreeNode node)
    {
        TreeNode top = node;

        while (top.Parent is not null)
            top = top.Parent;

        return ReferenceEquals(top, _root) && !ReferenceEquals(node, _root);
    }

    /// <summary>While renaming, Enter and Escape are the editor's: they end the edit
    /// before the text box sees them — Enter would otherwise press the default button.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_editing is null) return;

        switch (e.Key)
        {
            case Key.Enter:
                EndEdit(cancel: false);
                e.Handled = true;
                break;

            case Key.Escape:
                EndEdit(cancel: true);
                e.Handled = true;
                break;
        }
    }

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
        // the editor's own keys — arrows, Home, End — bubble up here unhandled
        // and must not move the selection under it
        if (_editing is not null) return;

        if (_flat.Count == 0) return;

        TreeNode? current = SelectedNode;
        int row = RowOf(current);

        // Shift with a move extends the selection from the anchor, as in Explorer
        bool extend = SelectionMode == SelectionMode.Extended && e.Modifiers.HasFlag(KeyModifiers.Shift);

        switch (e.Key)
        {
            case Key.Down:
                SelectRow(row + 1, extend);
                break;

            case Key.Up:
                // focus without a selection — the first arrow picks the edge
                // rather than jumping into nowhere
                SelectRow(row < 0 ? _flat.Count - 1 : row - 1, extend);
                break;

            case Key.Home:
                SelectRow(0, extend);
                break;

            case Key.End:
                SelectRow(_flat.Count - 1, extend);
                break;

            case Key.PageDown:
                SelectRow(row < 0 ? 0 : row + PageRows, extend);
                break;

            case Key.PageUp:
                SelectRow(row < 0 ? 0 : row - PageRows, extend);
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
                if (current is null) return;

                Invoke(current);
                break;

            // with check boxes Space checks, as in every tree with them;
            // without, it toggles the node as Enter used to
            case Key.Space when CheckBoxes:
                if (current is null) return;

                ToggleCheckedSelection();
                break;

            case Key.Space:
                if (current is not { HasChildren: true }) return;

                current.IsExpanded = !current.IsExpanded;
                break;

            case Key.F2 when LabelEdit && e.Modifiers == KeyModifiers.None:
                if (current is null) return;

                BeginEdit(current);
                break;

            case Key.A when e.Modifiers == KeyModifiers.Control && SelectionMode != SelectionMode.Single:
                SelectAll();
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private void SelectRow(int row, bool extend = false)
    {
        if (_flat.Count == 0) return;

        var node = (TreeNode)_flat[Math.Clamp(row, 0, _flat.Count - 1)];

        if (extend)
        {
            SelectRange(node);
            ScrollIntoView(node);
        }
        else
        {
            SelectNode(node);
        }
    }

    private void SelectNode(TreeNode node)
    {
        SelectOnly(node);

        ScrollIntoView(node);
    }

    /// <summary>Bring a node into the visible part. A node under a collapsed
    /// ancestor is not shown at all — there is nowhere to bring it.</summary>
    /// <remarks>Works right after <see cref="TreeNode.ExpandAncestors"/> too: the
    /// scroll is done again on the next layout, when the rows the expand added
    /// are measured and the extent reaches the node.</remarks>
    public void ScrollIntoView(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        _reveal = node;
        Reveal(node, _panel.ActualSize.Height);

        // the layout that finishes the scroll comes even when nothing had to move now
        Invalidate();
    }

    /// <summary>Scroll so the node's row is inside a viewport of this height.</summary>
    private void Reveal(TreeNode node, float viewport)
    {
        int row = RowOf(node);
        if (row < 0 || ItemHeight <= 0 || viewport <= 0) return;

        float top = row * ItemHeight;
        float bottom = top + ItemHeight;

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

        // the editor is placed over a row, it takes no room of its own
        _editor?.Measure(new Size(inner.Width, ItemHeight));

        var content = new Size(
            _panel.DesiredSize.Width + Padding.Horizontal,
            _panel.DesiredSize.Height + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        // the panel is measured with every row by now: the scroll asked for before
        // the measure reaches its node. Before the panel's arrange — the rows it
        // realizes are those of the scroll position
        if (_reveal is { } reveal)
        {
            _reveal = null;
            Reveal(reveal, Math.Max(0, contentSize.Height - Padding.Vertical));
        }
        _panel.Arrange(new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, contentSize.Width - Padding.Horizontal),
                Math.Max(0, contentSize.Height - Padding.Vertical))));

        ArrangeEditor();
    }

    /// <summary>The editor over the caption of its node's row. A row scrolled out of
    /// sight has no place: the editor waits with no size until it comes back.</summary>
    private void ArrangeEditor()
    {
        if (_editor is null || _editing is null) return;

        Point origin = GetAbsolutePosition();

        foreach (UIElement child in _panel.Children)
        {
            if (child is not TreeViewItem row || !ReferenceEquals(row.Node, _editing)) continue;

            Rectangle caption = row.CaptionBounds;
            Point at = row.GetAbsolutePosition();

            float x = at.X - origin.X + caption.X;
            float y = at.Y - origin.Y;
            float right = Math.Max(x, ActualSize.Width - Padding.Right);

            _editor.Arrange(new Rectangle(new Point(x, y), new Size(right - x, row.ActualSize.Height)));
            return;
        }

        _editor.Arrange(new Rectangle(Point.Empty, Size.Empty));
    }
}