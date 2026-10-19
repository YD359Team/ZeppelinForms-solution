using System.Runtime.CompilerServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Text;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>Containers only for the items in sight, when the rows are all of one height.</summary>
/// <remarks>
/// <para>
/// Without it a container is made and measured for every item: ten thousand labels
/// for a list of ten thousand lines, all of them measured on every layout. With it
/// the list has the containers of the rows in sight and a few around them; the rest
/// are made as they scroll in, and labels are reused as they scroll out. The engine
/// is the one the tree scrolls with (<see cref="ItemRealizer"/>).
/// </para>
/// <para>
/// The rows must be of one height: <see cref="ItemHeight"/>, or, left NaN, the
/// height of the first row there is. A list of rows of different heights — cards,
/// a feed — keeps a container per item with IsVirtualizing off.
/// </para>
/// <para>
/// <see cref="ContainerFromIndex"/> and <see cref="IndexFromContainer"/> find a
/// row's container either way: with virtualization the children are no longer one
/// per item, and a child's index in <c>Children</c> is not its item's index.
/// </para>
/// </remarks>
public partial class ItemsControl
{
    private const int OverscanCount = 3;

    /// <summary>The first guess at a row's height, before a row was measured.</summary>
    private const float GuessedItemHeight = 24f;

    private ItemRealizer? _realizer;

    /// <summary>The text labels the list made itself: only they may be given another
    /// item. Weak — a label that left the list is not kept for this.</summary>
    private readonly ConditionalWeakTable<UIElement, object> _ownLabels = [];

    /// <summary>The height of a row as last measured, when <see cref="ItemHeight"/> is NaN.</summary>
    private float _measuredItemHeight;

    private ItemRealizer Realizer => _realizer ??= new ItemRealizer(
        this,
        CreateVirtualContainer,
        container => ItemTemplate is null && _ownLabels.TryGetValue(container, out _));

    /// <summary>Make containers only for the items in sight. False here; a list box
    /// turns it on.</summary>
    public bool IsVirtualizing
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            if (value)
            {
                // the containers of every item go; the ones in sight come back at the
                // next layout
                Children.Clear();
                _containers.Clear();
                Realizer.Invalidate();
            }
            else
            {
                _realizer?.RecycleAll();
                _realizer?.ClearPool();
                RegenerateContainers();
            }

            Invalidate();
        }
    }

    /// <summary>The height of every row while virtualizing; NaN — the height of the
    /// first row there is, measured. Ignored without virtualization.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not positive, and not NaN.</exception>
    public float ItemHeight
    {
        get;
        set
        {
            if (!float.IsNaN(value) && !(value > 0 && float.IsFinite(value)))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Expected a positive height, or NaN to measure it.");

            if (field.Equals(value)) return;

            field = value;
            _realizer?.Invalidate();
            Invalidate();
        }
    } = float.NaN;

    /// <summary>The row height in use: the one set, the one measured, or a guess.</summary>
    private float EffectiveItemHeight =>
        !float.IsNaN(ItemHeight) ? ItemHeight
        : _measuredItemHeight > 0 ? _measuredItemHeight
        : GuessedItemHeight;

    /// <summary>Past the end the last row stops at the top: a row, not a wheel step.</summary>
    protected override float ScrollPastEndRow => IsVirtualizing ? EffectiveItemHeight : base.ScrollPastEndRow;

    /// <summary>The container of the item at an index; null — there is none: the index
    /// is out of range, or the row is out of sight in a virtualizing list.</summary>
    public UIElement? ContainerFromIndex(int index)
    {
        if (index < 0 || index >= Items.Count) return null;

        if (IsVirtualizing) return Realizer.ContainerAt(index);

        return index < Children.Count ? Children[index] : null;
    }

    /// <summary>The index of the item a container shows; −1 — not a container of this list.</summary>
    public int IndexFromContainer(UIElement container)
    {
        ArgumentNullException.ThrowIfNull(container);

        return IsVirtualizing ? Realizer.IndexOf(container) : Children.IndexOf(container);
    }

    /// <summary>The containers there are, with the index of their item: every one
    /// without virtualization, the ones in sight with it.</summary>
    internal IEnumerable<(int Index, UIElement Container)> RealizedContainers
    {
        get
        {
            if (IsVirtualizing)
            {
                foreach ((int index, UIElement container) in Realizer.Realized)
                    yield return (index, container);

                yield break;
            }

            for (int i = 0; i < Children.Count; i++)
                yield return (i, Children[i]);
        }
    }

    /// <summary>Scroll so that the row of an item is in sight; nothing if it is already.
    /// A virtualizing list makes the row's container in the next layout.</summary>
    public void ScrollIntoView(int index)
    {
        if (index < 0 || index >= Items.Count) return;

        float top, height;

        if (IsVirtualizing)
        {
            height = EffectiveItemHeight;
            top = Padding.Top + index * height;
        }
        else
        {
            if (index >= Children.Count) return;

            top = Padding.Top;

            for (int i = 0; i < index; i++)
                if (Children[i].IsVisible)
                    top += Children[i].ActualSize.Height;

            height = Children[index].ActualSize.Height;
        }

        float visible = Viewport.Height;

        if (top < ScrollY) ScrollTo(ScrollX, top);
        else if (top + height > ScrollY + visible) ScrollTo(ScrollX, top + height - visible);
    }

    private UIElement CreateVirtualContainer(object item)
    {
        UIElement container = CreateContainer(item);

        // the list's own label, not the item itself nor a template's: only it may
        // be rebound to another item
        if (ItemTemplate is null && item is not UIElement)
            _ownLabels.AddOrUpdate(container, item);

        return container;
    }

    private Size MeasureVirtualized(Size availableSize)
    {
        // along the scrollable axis PanelControl gives infinity, and the real window
        // height is known only from the previous arrange. Twenty rows is just
        // a guess for the very first pass: the arrange will correct it
        float viewport = float.IsFinite(availableSize.Height)
            ? availableSize.Height
            : ArrangedViewport.Height > 0 ? ArrangedViewport.Height : EffectiveItemHeight * 20;

        float width = Math.Max(0, availableSize.Width - Padding.Horizontal);
        float maxWidth = MeasureRealized(viewport, width);

        // the height of the first row there is: a guess off by more than a hair
        // changes how many rows are in sight, and the range is built again with it
        if (float.IsNaN(ItemHeight) && Realizer.Realized.Count > 0)
        {
            float measured = 0;

            foreach (UIElement container in Realizer.Realized.Values)
                measured = Math.Max(measured, container.DesiredSize.Height);

            if (measured > 0 && Math.Abs(measured - _measuredItemHeight) > 0.01f)
            {
                _measuredItemHeight = measured;
                Realizer.Invalidate();
                maxWidth = MeasureRealized(viewport, width);
            }
        }

        // the height is computed for the whole list, not for the created rows —
        // otherwise the scrollbar would lie
        var content = new Size(maxWidth + Padding.Horizontal, Items.Count * EffectiveItemHeight + Padding.Vertical);
        return ResolveSize(content, availableSize);
    }

    /// <summary>Bring the containers to the window and measure them; the widest.</summary>
    private float MeasureRealized(float viewport, float width)
    {
        Realizer.Update(Items, ScrollY, viewport, EffectiveItemHeight, OverscanCount);

        // rows are measured at their own height when it isn't set: that is how it is learned
        var itemSize = new Size(width, float.IsNaN(ItemHeight) ? float.PositiveInfinity : ItemHeight);
        float maxWidth = 0;

        foreach (UIElement container in Realizer.Realized.Values)
        {
            container.Measure(itemSize);
            maxWidth = Math.Max(maxWidth, container.DesiredSize.Width);
        }

        return maxWidth;
    }

    private void ArrangeVirtualized(Size contentSize)
    {
        float width = Math.Max(0, contentSize.Width - Padding.Horizontal);
        float height = EffectiveItemHeight;

        // here the window height is already exact. If the measure's guess was off —
        // the window grew, the list is arranged for the first time — the rows are
        // made right away and measured here rather than asking for a second pass
        if (Realizer.Update(Items, ScrollY, ArrangedViewport.Height, height, OverscanCount))
        {
            var itemSize = new Size(width, height);

            foreach (UIElement container in Realizer.Realized.Values)
                container.Measure(itemSize);
        }

        foreach ((int index, UIElement container) in Realizer.Realized)
        {
            container.Arrange(new Rectangle(
                new Point(Padding.Left, Padding.Top + index * height),
                new Size(width, height)));
        }
    }
}
