using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using ZeppelinForms.Core.Collections;

namespace ZeppelinForms.Forms.Controls.Tree;

/// <summary>What a source makes its nodes in: a <see cref="TreeView"/> or a <see cref="TreeDataGrid"/>.</summary>
internal interface ITreeHost
{
    IList<TreeNode> Nodes { get; }

    void BeginUpdate();

    void EndUpdate();
}

/// <summary>
/// The nodes of a tree made from data: the view model's own objects and their children,
/// see <see cref="TreeView.ItemsSource"/> and <see cref="TreeDataGrid.ItemsSource"/>.
/// Made with <see cref="Create"/>.
/// </summary>
/// <example>
/// <code>
/// tree.ItemsSource = TreeItemsSource.Create(vm.Departments, d => d.Children)
///     .WithExpanded(d => d.IsOpen, (d, open) => d.IsOpen = open)
///     .WithChecked(d => d.IsIncluded, (d, included) => d.IsIncluded = included);
/// </code>
/// </example>
public abstract class TreeItemsSource
{
    private protected TreeItemsSource()
    {
    }

    /// <summary>A tree of these roots, each node's children given by <paramref name="children"/>.</summary>
    /// <param name="roots">The top-level items. An <see cref="INotifyCollectionChanged"/> —
    /// an <c>ObservableCollection</c> — is followed: an item added shows as a node.</param>
    /// <param name="children">An item's children; null or empty — a leaf. Followed as the
    /// roots are, and read only when the item's node is expanded the first time: a big
    /// tree is made as far as it is opened.</param>
    public static TreeItemsSource<T> Create<T>(IEnumerable<T> roots, Func<T, IEnumerable<T>?> children)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(children);

        return new TreeItemsSource<T>(roots, children);
    }

    /// <summary>The source reads the check states of its items: their children's states are
    /// theirs, not a parent's handed down.</summary>
    internal abstract bool ReadsChecked { get; }

    /// <summary>The tree is in a form: the nodes are put in step with the data, and the
    /// data is listened to.</summary>
    internal abstract void Attach(ITreeHost tree);

    /// <summary>The tree left its form, or the source was replaced: the data is no longer
    /// listened to and holds nothing of the tree. The nodes stay.</summary>
    internal abstract void Detach();

    /// <summary>The nodes made from the data before the tree is in a form, listening to
    /// nothing yet.</summary>
    internal abstract void Fill(ITreeHost tree);

    /// <summary>A node of the source is being expanded: its children are made now, the
    /// first time.</summary>
    internal abstract void Realize(TreeNode node);
}

/// <summary>A tree of items of one type, see <see cref="TreeItemsSource.Create"/>.</summary>
public sealed class TreeItemsSource<T> : TreeItemsSource where T : class
{
    private readonly IEnumerable<T> _roots;
    private readonly Func<T, IEnumerable<T>?> _children;

    private Func<T, bool>? _getExpanded;
    private Action<T, bool>? _setExpanded;
    private Func<T, bool>? _getChecked;
    private Action<T, bool>? _setChecked;

    /// <summary>What the source keeps of a node it made.</summary>
    private sealed class NodeInfo(T model)
    {
        public T Model { get; } = model;

        /// <summary>The children the node was made with, if they tell about their changes.</summary>
        public IEnumerable<T>? Children { get; set; }

        /// <summary>The children are nodes: the node was expanded once, or made expanded.</summary>
        public bool Realized { get; set; }

        public NotifyCollectionChangedEventHandler? ChildrenHandler { get; set; }
    }

    private readonly Dictionary<TreeNode, NodeInfo> _nodes = new(ReferenceEqualityComparer.Instance);

    /// <summary>The nodes of each item: the same item may stand in two places of a tree.</summary>
    private readonly Dictionary<T, List<TreeNode>> _byModel = new(ReferenceEqualityComparer.Instance);

    private ITreeHost? _tree;

    /// <summary>The source listens to the data: the tree is in a form.</summary>
    private bool _listening;

    /// <summary>A node's state is being written into its item: the item telling of the
    /// change it got is not read back — a setter that rounds the value would ping-pong.</summary>
    private T? _writing;

    internal TreeItemsSource(IEnumerable<T> roots, Func<T, IEnumerable<T>?> children)
    {
        _roots = roots;
        _children = children;
    }

    /// <summary>Expand and collapse by the items: a node is expanded while
    /// <paramref name="get"/> says so, and <paramref name="set"/> — when given — hears of
    /// the user expanding or collapsing it. An item telling of its changes
    /// (<see cref="INotifyPropertyChanged"/>) is read again on each.</summary>
    public TreeItemsSource<T> WithExpanded(Func<T, bool> get, Action<T, bool>? set = null)
    {
        ArgumentNullException.ThrowIfNull(get);

        _getExpanded = get;
        _setExpanded = set;
        return this;
    }

    /// <summary>Check boxes by the items, as <see cref="WithExpanded"/> does expansion. The
    /// items' own states are the truth: under <see cref="CheckPropagation.Cascade"/> a
    /// parent shows the states its children's items have, and is written back as the
    /// tree sees it — an item whose children are only partly checked gets false.</summary>
    public TreeItemsSource<T> WithChecked(Func<T, bool> get, Action<T, bool>? set = null)
    {
        ArgumentNullException.ThrowIfNull(get);

        _getChecked = get;
        _setChecked = set;
        return this;
    }

    internal override bool ReadsChecked => _getChecked is not null;

    // ===== the tree's side =====

    internal override void Fill(ITreeHost tree)
    {
        _tree = tree;
        Sync(null, _roots);
    }

    internal override void Attach(ITreeHost tree)
    {
        _tree = tree;

        if (!_listening)
        {
            _listening = true;

            if (_roots is INotifyCollectionChanged roots)
                roots.CollectionChanged += OnRootsChanged;
        }

        // what changed while nobody listened, all the way down
        Sync(null, _roots, deep: true);

        foreach ((TreeNode node, NodeInfo info) in _nodes.ToArray())
            Listen(node, info);

        PushStates();
    }

    /// <summary>The items hear what their nodes became while nobody listened: a parent
    /// the tree showed checked by its children — read back unchanged, an item left false
    /// would uncheck them all on its next change.</summary>
    private void PushStates()
    {
        foreach ((TreeNode node, NodeInfo info) in _nodes.ToArray())
        {
            if (_getChecked is { } isChecked && _setChecked is { } setChecked && isChecked(info.Model) != node.IsChecked)
                Write(info.Model, model => setChecked(model, node.IsChecked));

            if (_getExpanded is { } isExpanded && _setExpanded is { } setExpanded && isExpanded(info.Model) != node.IsExpanded)
                Write(info.Model, model => setExpanded(model, node.IsExpanded));
        }
    }

    internal override void Detach()
    {
        if (!_listening) return;

        _listening = false;

        if (_roots is INotifyCollectionChanged roots)
            roots.CollectionChanged -= OnRootsChanged;

        foreach ((TreeNode node, NodeInfo info) in _nodes)
            Unlisten(node, info);
    }

    internal override void Realize(TreeNode node)
    {
        if (!_nodes.TryGetValue(node, out NodeInfo? info) || info.Realized) return;

        info.Realized = true;

        Sync(node, info.Children ?? _children(info.Model));

        node.HasUnrealizedChildren = false;
    }

    // ===== nodes =====

    private TreeNode Make(T model)
    {
        var node = new TreeNode(model);
        var info = new NodeInfo(model) { Children = _children(model) };

        _nodes[node] = info;

        if (!_byModel.TryGetValue(model, out List<TreeNode>? nodes))
            _byModel[model] = nodes = [];

        nodes.Add(node);

        // the expander without making the children: only whether there are any
        node.HasUnrealizedChildren = HasAny(info.Children);

        if (_getChecked is { } isChecked)
            node.IsChecked = isChecked(model);

        if (_listening) Listen(node, info);

        return node;
    }

    /// <summary>The state an item gives its node, once the node is in the tree: an
    /// expanded one makes its children through the expansion, as a click would.</summary>
    private void ReadExpanded(TreeNode node, T model)
    {
        if (_getExpanded is { } isExpanded && isExpanded(model) != node.IsExpanded)
            node.IsExpanded = isExpanded(model);
    }

    private void Release(TreeNode node)
    {
        Stack<TreeNode> pending = new();
        pending.Push(node);

        while (pending.Count > 0)
        {
            TreeNode current = pending.Pop();

            if (!_nodes.Remove(current, out NodeInfo? info)) continue;

            Unlisten(current, info);

            if (_byModel.TryGetValue(info.Model, out List<TreeNode>? nodes))
            {
                nodes.Remove(current);
                if (nodes.Count == 0) _byModel.Remove(info.Model);
            }

            foreach (TreeNode child in current.Children)
                pending.Push(child);
        }
    }

    /// <summary>Put a level in step with its items: the nodes of the items still there
    /// stay — and keep their expansion, selection and children —, the others go, new
    /// items get nodes, and the order becomes the items'.</summary>
    /// <param name="parent">Null — the top level, the tree's own nodes.</param>
    /// <param name="deep">The levels under the kept nodes too: after the data went
    /// unheard. Otherwise a level below is read again only when its collection is
    /// another one — a change of the collection itself tells of itself.</param>
    private void Sync(TreeNode? parent, IEnumerable<T>? models, bool deep = false)
    {
        if (_tree is not { } tree) return;

        IList<TreeNode> level = parent?.Children ?? tree.Nodes;

        // the nodes there are, by item: each node is taken once
        Dictionary<T, Queue<TreeNode>> present = new(ReferenceEqualityComparer.Instance);

        foreach (TreeNode node in level)
        {
            if (!_nodes.TryGetValue(node, out NodeInfo? info)) continue;

            if (!present.TryGetValue(info.Model, out Queue<TreeNode>? queue))
                present[info.Model] = queue = new Queue<TreeNode>();

            queue.Enqueue(node);
        }

        List<TreeNode> wanted = [];
        List<TreeNode> made = [];

        foreach (T model in models ?? [])
        {
            if (present.TryGetValue(model, out Queue<TreeNode>? queue) && queue.Count > 0)
            {
                TreeNode kept = queue.Dequeue();
                wanted.Add(kept);

                // a kept node follows its item's children — the item may have new ones
                // — and, expanded once, follows them further down
                NodeInfo info = _nodes[kept];
                bool replaced = RefreshChildren(kept, info);

                if (info.Realized && (replaced || deep)) Sync(kept, info.Children, deep);
            }
            else
            {
                TreeNode node = Make(model);
                wanted.Add(node);
                made.Add(node);
            }
        }

        var keep = new HashSet<TreeNode>(wanted, ReferenceEqualityComparer.Instance);

        tree.BeginUpdate();

        try
        {
            // the gone first, from the end: each removal tells which node it was
            for (int i = level.Count - 1; i >= 0; i--)
            {
                if (keep.Contains(level[i])) continue;

                TreeNode gone = level[i];
                level.RemoveAt(i);
                Release(gone);
            }

            for (int i = 0; i < wanted.Count; i++)
            {
                if (i < level.Count && ReferenceEquals(level[i], wanted[i])) continue;

                int at = IndexOf(level, wanted[i], i + 1);

                if (at >= 0 && level is ZfCollection<TreeNode> collection) collection.Move(at, i);
                else if (at >= 0) { level.RemoveAt(at); level.Insert(i, wanted[i]); }
                else level.Insert(i, wanted[i]);
            }
        }
        finally
        {
            tree.EndUpdate();
        }

        // once in the tree: an expansion makes the children as a click would
        foreach (TreeNode node in made)
            ReadExpanded(node, _nodes[node].Model);
    }

    /// <summary>The item's children read again: an item may give another collection
    /// for them — a view model that replaces its list rather than changing it.</summary>
    /// <returns>Whether the collection is another one.</returns>
    private bool RefreshChildren(TreeNode node, NodeInfo info)
    {
        IEnumerable<T>? children = _children(info.Model);

        if (ReferenceEquals(children, info.Children)) return false;

        if (info.Children is INotifyCollectionChanged old && info.ChildrenHandler is { } handler)
        {
            old.CollectionChanged -= handler;
            info.ChildrenHandler = null;
        }

        info.Children = children;

        if (_listening) Listen(node, info);

        if (!info.Realized) node.HasUnrealizedChildren = HasAny(children);

        return true;
    }

    private static int IndexOf(IList<TreeNode> level, TreeNode node, int from)
    {
        for (int i = from; i < level.Count; i++)
            if (ReferenceEquals(level[i], node)) return i;

        return -1;
    }

    private static bool HasAny(IEnumerable<T>? items) => items switch
    {
        null => false,
        ICollection collection => collection.Count > 0,
        IReadOnlyCollection<T> collection => collection.Count > 0,
        _ => items.Any(),
    };

    // ===== listening =====

    private void Listen(TreeNode node, NodeInfo info)
    {
        node.PropertyChanged -= OnNodeChanged;
        node.PropertyChanged += OnNodeChanged;

        if (info.Model is INotifyPropertyChanged model)
        {
            model.PropertyChanged -= OnModelChanged;
            model.PropertyChanged += OnModelChanged;
        }

        if (info.Children is INotifyCollectionChanged children && info.ChildrenHandler is null)
        {
            info.ChildrenHandler = (_, e) => OnChildrenChanged(node, info, e);
            children.CollectionChanged += info.ChildrenHandler;
        }
    }

    private void Unlisten(TreeNode node, NodeInfo info)
    {
        node.PropertyChanged -= OnNodeChanged;

        // the item may stand in another place still listened to: it stays heard there
        if (info.Model is INotifyPropertyChanged model && !IsListenedElsewhere(info.Model, node))
            model.PropertyChanged -= OnModelChanged;

        if (info.Children is INotifyCollectionChanged children && info.ChildrenHandler is { } handler)
        {
            children.CollectionChanged -= handler;
            info.ChildrenHandler = null;
        }
    }

    private bool IsListenedElsewhere(T model, TreeNode node) =>
        _listening && _byModel.TryGetValue(model, out List<TreeNode>? nodes) &&
        nodes.Any(other => !ReferenceEquals(other, node) && _nodes.ContainsKey(other));

    private void OnRootsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Sync(null, _roots);

    private void OnChildrenChanged(TreeNode node, NodeInfo info, NotifyCollectionChangedEventArgs e)
    {
        if (info.Realized)
            Sync(node, info.Children);
        else
            node.HasUnrealizedChildren = HasAny(info.Children);
    }

    /// <summary>An item changed: its nodes show it, and take its expansion and check.</summary>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not T model || ReferenceEquals(model, _writing)) return;

        if (!_byModel.TryGetValue(model, out List<TreeNode>? nodes)) return;

        foreach (TreeNode node in nodes.ToArray())
        {
            node.NotifyContentChanged();

            // the property that changed may be the children's
            if (_nodes.TryGetValue(node, out NodeInfo? info) && RefreshChildren(node, info) && info.Realized)
                Sync(node, info.Children);

            ReadExpanded(node, model);

            if (_getChecked is not { } isChecked) continue;

            bool value = isChecked(model);

            // an item false under a node in the third state is that state written back:
            // read as unchecked, it would uncheck the whole subtree
            if (!value && node.CheckedState == CheckedState.Intermediate) continue;

            node.IsChecked = value;
        }
    }

    /// <summary>A node changed in the tree — the user expanded or checked it, or the
    /// tree's propagation did: its item hears of it.</summary>
    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TreeNode node || !_nodes.TryGetValue(node, out NodeInfo? info)) return;

        switch (e.PropertyName)
        {
            case nameof(TreeNode.IsExpanded) when _setExpanded is { } setExpanded:
                Write(info.Model, model => setExpanded(model, node.IsExpanded));
                break;

            case nameof(TreeNode.CheckedState) when _setChecked is { } setChecked:
                Write(info.Model, model => setChecked(model, node.IsChecked));
                break;
        }
    }

    private void Write(T model, Action<T> write)
    {
        T? outer = _writing;
        _writing = model;

        try
        {
            write(model);
        }
        finally
        {
            _writing = outer;
        }
    }
}
