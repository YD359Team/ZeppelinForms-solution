using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Creates containers only for visible items. Requires rows of equal
/// height — otherwise the visible range can't be computed without measuring all of them.
/// </summary>
/// <remarks>
/// A container belongs to an item, not to a position in the list. When the
/// source's contents change — a tree node expanded, a row inserted above the
/// visible window — containers already created are not recreated but simply
/// move to their new index. Recreating by index detached the whole window of
/// rows from the tree and built it again: hover and press were lost, the theme
/// was reapplied, and the row that was clicked vanished right inside its own OnClick.
/// </remarks>
public class VirtualizingStackPanel : DecoratedPanel
{
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

    public IList<object> ItemsSource
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;

            // a new source is a new set of items: the range must be rebuilt,
            // as with Refresh. Previously the list kept showing the old rows
            // until someone called Refresh by hand
            Refresh();
        }
    } = [];

    public Func<object, UIElement>? ItemTemplate
    {
        get;
        set
        {
            if (field == value) return;

            // containers of the previous template don't fit the new one,
            // and a pool of labels is useless to a template — reset both
            RecycleAll();
            _recycled.Clear();

            field = value;
            _rangeValid = false;

            Invalidate();
        }
    }

    /// <summary>Row height. Equal for all — virtualization is built on that.</summary>
    public float ItemHeight
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // both the total size and the visible range depend on the row height
            _rangeValid = false;
            Invalidate();
        }
    } = 24f;

    /// <summary>How many rows to prepare beyond the visible ones,
    /// so that scrolling doesn't flicker.</summary>
    public int OverscanCount
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            _rangeValid = false;
            Invalidate();
        }
    } = 3;

    public VirtualizingStackPanel()
    {
        OverflowY = Overflow.Auto;
    }

    /// <summary>The contents or order of ItemsSource changed.</summary>
    public void Refresh()
    {
        // children are not touched here: clearing them now means showing an empty
        // list in the frame between Refresh and the next measure — that is
        // the flicker. It is enough to declare the range invalid,
        // and UpdateRealizedRange rebuilds it in the same layout pass
        _rangeValid = false;

        Invalidate();
    }

    private void RecycleAll()
    {
        SuppressChildrenInvalidate++;

        try
        {
            foreach (UIElement container in _realized.Values)
            {
                // a full rebuild is not the user removing rows,
                // there is no point seeing each one off with a disappearance
                container.SkipNextVisibilityTransitions();

                Children.Remove(container);
                _itemOf.Remove(container);
                Recycle(container);
            }

            _realized.Clear();
        }
        finally
        {
            SuppressChildrenInvalidate--;
        }
    }

    /// <summary>Return a container to the pool — or throw it away if the pool
    /// is useless. A custom template doesn't fit a reused container
    /// (Reuse knows only Label), so such containers are never taken back,
    /// and storing them means hoarding garbage until the window closes.</summary>
    private void Recycle(UIElement container)
    {
        if (ItemTemplate is not null) return;

        _recycled.Push(container);
    }

    // the text color is not set here: it comes from the theme, and a hard-coded
    // black made the rows invisible on a dark background
    private UIElement CreateContainer(object item) =>
        ItemTemplate?.Invoke(item) ?? new Label
        {
            Text = item?.ToString() ?? string.Empty,
            HorizontalContentAlign = HorizontalContentAlignment.Left,
            VerticalContentAlign = VerticalContentAlignment.Center,
            Padding = new Thickness(6, 3),
        };

    /// <summary>Bring the set of containers to the visible window.</summary>
    /// <returns>true if the set of containers changed and they need measuring.</returns>
    private bool UpdateRealizedRange(float viewportHeight)
    {
        if (ItemsSource.Count == 0 || ItemHeight <= 0)
        {
            bool hadContainers = _realized.Count > 0;

            RecycleAll();
            _rangeValid = false;

            return hadContainers;
        }

        int total = ItemsSource.Count;

        // ScrollY here may be from the previous contents: the list has just
        // collapsed, and PanelControl clamps the scroll only when arranging.
        // Without a clamp first ran past the end, and count went negative
        int first = Math.Clamp((int)(ScrollY / ItemHeight) - OverscanCount, 0, total - 1);
        int count = (int)Math.Ceiling(viewportHeight / ItemHeight) + OverscanCount * 2;
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
        HashSet<object>? present = !scrolling && ChildrenExitTransition is not null
            ? new HashSet<object>(ItemsSource.Where(item => item is not null), ReferenceEqualityComparer.Instance)
            : null;

        SuppressChildrenInvalidate++;

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
                object item = ItemsSource[i];

                if (item is not null && _byItem.Remove(item, out UIElement? kept))
                    _next[i] = kept;
                else
                    _missing.Add(i);
            }

            // whatever didn't make it into the new window is free
            _unmatched.AddRange(_byItem.Values);
            _byItem.Clear();

            // 3. the missing rows. Without a template a free label is rebound right
            // in place — there is no point detaching it from the tree only to
            // attach it back immediately
            int free = 0;

            foreach (int index in _missing)
            {
                object item = ItemsSource[index];
                UIElement container;

                if (ItemTemplate is null && free < _unmatched.Count)
                {
                    container = Reuse(_unmatched[free++], item);

                    // the container changed its item rather than moved: there is
                    // no point animating its trip from the old index to the new one
                    container.SkipNextLayoutTransition();
                }
                else if (ItemTemplate is null && _recycled.Count > 0)
                {
                    container = Reuse(_recycled.Pop(), item);
                    container.SkipNextLayoutTransition();
                    if (scrolling) container.SkipNextVisibilityTransitions();
                    Children.Add(container);
                }
                else
                {
                    container = CreateContainer(item);
                    if (scrolling) container.SkipNextVisibilityTransitions();
                    Children.Add(container);
                }

                _itemOf[container] = item;
                _next[index] = container;
            }

            // 4. whatever left the window goes into reuse
            for (; free < _unmatched.Count; free++)
            {
                UIElement container = _unmatched[free];

                // rolled out beyond the edge — didn't disappear. Only a row
                // whose item is no longer in the source disappears
                if (scrolling ||
                    (present is not null &&
                     _itemOf.GetValueOrDefault(container) is { } gone &&
                     present.Contains(gone)))
                    container.SkipNextVisibilityTransitions();

                Children.Remove(container);
                _itemOf.Remove(container);
                Recycle(container);
            }

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

            SuppressChildrenInvalidate--;
        }
    }

    private static UIElement Reuse(UIElement container, object item)
    {
        if (container is Label label)
            label.Text = item?.ToString() ?? string.Empty;

        return container;
    }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        // along the scrollable axis PanelControl gives infinity, and the real window
        // height is known only from the previous arrange. Twenty rows is just
        // a guess for the very first pass: the arrange will correct it
        float viewportHeight = float.IsFinite(availableSize.Height)
            ? availableSize.Height
            : ArrangedViewport.Height > 0 ? ArrangedViewport.Height : ItemHeight * 20;

        UpdateRealizedRange(viewportHeight);

        var itemSize = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            ItemHeight);

        float maxWidth = 0;

        foreach (UIElement container in _realized.Values)
        {
            container.Measure(itemSize);
            maxWidth = Math.Max(maxWidth, container.DesiredSize.Width);
        }

        // the height is computed for the whole list, not for the created rows —
        // otherwise the scrollbar would lie
        return new Size(
            maxWidth + Padding.Horizontal,
            ItemsSource.Count * ItemHeight + Padding.Vertical);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        float width = Math.Max(0, contentSize.Width - Padding.Horizontal);

        // here the window height is already exact. If the measure's guess was
        // off — the window grew, the panel is arranged for the first time —
        // the rows are created right away rather than asking for a second pass:
        // the containers are measured right here. The panel's width under
        // auto-sizing isn't recomputed until the next pass — rows of equal
        // height hardly change it
        if (UpdateRealizedRange(ArrangedViewport.Height))
        {
            var itemSize = new Size(width, ItemHeight);

            foreach (UIElement container in _realized.Values)
                container.Measure(itemSize);
        }

        foreach ((int index, UIElement container) in _realized)
        {
            container.Arrange(new Rectangle(
                new Point(Padding.Left, Padding.Top + index * ItemHeight),
                new Size(width, ItemHeight)));
        }
    }
}