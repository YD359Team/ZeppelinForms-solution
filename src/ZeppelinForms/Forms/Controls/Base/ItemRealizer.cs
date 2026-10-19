using ZeppelinForms.Forms.Controls.Text;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Containers for the visible window of a list of items, all of one height: the ones
/// in sight are in the host's children, the rest don't exist. One engine for every
/// virtualizing list — the <see cref="VirtualizingStackPanel"/> a tree scrolls in and
/// the <see cref="ItemsControl"/> a list box is.
/// </summary>
/// <remarks>
/// <para>
/// A container belongs to an item, not to a position in the list. When the
/// source's contents change — a tree node expanded, a row inserted above the
/// visible window — containers already created are not recreated but simply
/// move to their new index. Recreating by index detached the whole window of
/// rows from the tree and built it again: hover and press were lost, the theme
/// was reapplied, and the row that was clicked vanished right inside its own OnClick.
/// </para>
/// <para>
/// Until 0.17 this lived in VirtualizingStackPanel alone, and a list box created a
/// container per item — ten thousand labels for ten thousand rows.
/// </para>
/// </remarks>
internal sealed class ItemRealizer
{
    private readonly PanelControl _host;
    private readonly Func<object, UIElement> _create;
    private readonly Func<UIElement, bool> _canReuse;

    private Dictionary<int, UIElement> _realized = [];
    private Dictionary<int, UIElement> _next = [];
    private readonly Stack<UIElement> _recycled = new();

    // the source item a container shows. Compared by reference: a tree node
    // with an overridden Equals must not take someone else's row
    private readonly Dictionary<UIElement, object?> _itemOf = new(ReferenceEqualityComparer.Instance);

    // working collections of the range rebuild — fields rather than locals,
    // so that scrolling doesn't allocate memory per row
    private readonly Dictionary<object, UIElement> _byItem = new(ReferenceEqualityComparer.Instance);
    private readonly List<UIElement> _unmatched = [];
    private readonly List<int> _missing = [];

    private int _firstVisible;
    private int _visibleCount;
    private bool _rangeValid;

    /// <param name="host">The panel whose children the containers are.</param>
    /// <param name="create">A new container for an item.</param>
    /// <param name="canReuse">Whether a container may be given another item: only a
    /// plain label of the host's own, whose text is all there is to it.</param>
    public ItemRealizer(PanelControl host, Func<object, UIElement> create, Func<UIElement, bool> canReuse)
    {
        _host = host;
        _create = create;
        _canReuse = canReuse;
    }

    /// <summary>The containers there are, by the index of their item.</summary>
    public IReadOnlyDictionary<int, UIElement> Realized => _realized;

    /// <summary>The container of the item at an index; null — it is out of sight.</summary>
    public UIElement? ContainerAt(int index) => _realized.GetValueOrDefault(index);

    /// <summary>The index of the item a container shows; −1 — not one of these.</summary>
    public int IndexOf(UIElement container)
    {
        foreach ((int index, UIElement realized) in _realized)
            if (ReferenceEquals(realized, container))
                return index;

        return -1;
    }

    /// <summary>The contents or order of the items changed: the range is rebuilt at
    /// the next update, the containers kept for the items still there.</summary>
    /// <remarks>
    /// Children are not touched here: clearing them now means showing an empty
    /// list in the frame between this and the next measure — that is the flicker.
    /// It is enough to declare the range invalid, and the update rebuilds it in the
    /// same layout pass.
    /// </remarks>
    public void Invalidate() => _rangeValid = false;

    /// <summary>Forget the pool: containers of a template that changed fit no longer.</summary>
    public void ClearPool() => _recycled.Clear();

    public void RecycleAll()
    {
        _host.SuppressChildrenInvalidate++;

        try
        {
            foreach (UIElement container in _realized.Values)
            {
                // a full rebuild is not the user removing rows,
                // there is no point seeing each one off with a disappearance
                container.SkipNextVisibilityTransitions();

                _host.Children.Remove(container);
                _itemOf.Remove(container);
                Recycle(container);
            }

            _realized.Clear();
        }
        finally
        {
            _host.SuppressChildrenInvalidate--;
        }
    }

    /// <summary>Return a container to the pool — or throw it away if the pool
    /// is useless. A custom template doesn't fit a reused container
    /// (Reuse knows only Label), so such containers are never taken back,
    /// and storing them means hoarding garbage until the window closes.</summary>
    private void Recycle(UIElement container)
    {
        if (!_canReuse(container)) return;

        _recycled.Push(container);
    }

    /// <summary>Bring the set of containers to the visible window.</summary>
    /// <returns>true if the set of containers changed and they need measuring.</returns>
    public bool Update(IList<object> items, float scrollY, float viewportHeight, float itemHeight, int overscan)
    {
        if (items.Count == 0 || itemHeight <= 0)
        {
            bool hadContainers = _realized.Count > 0;

            RecycleAll();
            _rangeValid = false;

            return hadContainers;
        }

        int total = items.Count;

        // ScrollY here may be from the previous contents: the list has just
        // collapsed, and PanelControl clamps the scroll only when arranging.
        // Without a clamp first ran past the end, and count went negative
        int first = Math.Clamp((int)(scrollY / itemHeight) - overscan, 0, total - 1);
        int count = (int)Math.Ceiling(viewportHeight / itemHeight) + overscan * 2;
        count = Math.Clamp(count, 0, total - first);

        // the early exit must look at _rangeValid: after Refresh the range is often
        // the same, but the items in it are different. Without this check
        // expanding a node left rows from the old projection
        if (_rangeValid && first == _firstVisible && count == _visibleCount)
            return false;

        // scrolling with the source's contents unchanged: rows don't appear
        // or disappear, they roll into the window and out of it. It becomes
        // appearing and disappearing only after Refresh
        bool scrolling = _rangeValid;

        // after Refresh an outgoing row may simply be pushed beyond the edge —
        // its item is still in the source. Only the row whose item was removed
        // should disappear. The set is needed only if disappearing is enabled
        HashSet<object>? present = !scrolling && _host.ChildrenExitTransition is not null
            ? new HashSet<object>(items.Where(item => item is not null), ReferenceEqualityComparer.Instance)
            : null;

        _host.SuppressChildrenInvalidate++;

        try
        {
            // 1. the previous containers by their items. A repeat of the same
            // reference in the source (one row twice) gets a separate container
            foreach (UIElement container in _realized.Values)
            {
                object? item = _itemOf.GetValueOrDefault(container);

                if (item is null || !_byItem.TryAdd(item, container))
                    _unmatched.Add(container);
            }

            // 2. the new window: everything that was visible stays the same object
            // and just moves to its new index
            for (int i = first; i < first + count; i++)
            {
                object item = items[i];

                if (item is not null && _byItem.Remove(item, out UIElement? kept))
                    _next[i] = kept;
                else
                    _missing.Add(i);
            }

            // whatever didn't make it into the new window is free
            _unmatched.AddRange(_byItem.Values);
            _byItem.Clear();

            // 3. the missing rows. A free label is rebound right in place —
            // there is no point detaching it from the tree only to attach it
            // back immediately
            int free = 0;

            foreach (int index in _missing)
            {
                object item = items[index];
                UIElement container;

                while (free < _unmatched.Count && !_canReuse(_unmatched[free]))
                    Release(_unmatched[free++], scrolling, present);

                if (free < _unmatched.Count)
                {
                    container = Reuse(_unmatched[free++], item);

                    // the container changed its item rather than moved: there is
                    // no point animating its trip from the old index to the new one
                    container.SkipNextLayoutTransition();
                }
                else if (_recycled.Count > 0)
                {
                    container = Reuse(_recycled.Pop(), item);
                    container.SkipNextLayoutTransition();
                    if (scrolling) container.SkipNextVisibilityTransitions();
                    _host.Children.Add(container);
                }
                else
                {
                    container = _create(item);
                    if (scrolling) container.SkipNextVisibilityTransitions();
                    _host.Children.Add(container);
                }

                _itemOf[container] = item;
                _next[index] = container;
            }

            // 4. whatever left the window goes into reuse
            for (; free < _unmatched.Count; free++)
                Release(_unmatched[free], scrolling, present);

            (_realized, _next) = (_next, _realized);
            _next.Clear();

            _rangeValid = true;
            _firstVisible = first;
            _visibleCount = count;

            return true;
        }
        finally
        {
            _unmatched.Clear();
            _missing.Clear();

            _host.SuppressChildrenInvalidate--;
        }
    }

    /// <summary>A container whose item left the window: out of the children, into
    /// the pool if it can be reused.</summary>
    private void Release(UIElement container, bool scrolling, HashSet<object>? present)
    {
        // rolled out beyond the edge — didn't disappear. Only a row
        // whose item is no longer in the source disappears
        if (scrolling ||
            (present is not null &&
             _itemOf.GetValueOrDefault(container) is { } gone &&
             present.Contains(gone)))
            container.SkipNextVisibilityTransitions();

        _host.Children.Remove(container);
        _itemOf.Remove(container);
        Recycle(container);
    }

    private static UIElement Reuse(UIElement container, object item)
    {
        if (container is Label label)
            label.Text = item?.ToString() ?? string.Empty;

        return container;
    }
}
