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
    /// <summary>The containers of the rows in sight (see <see cref="ItemRealizer"/>, the
    /// engine this panel shares with the list box).</summary>
    private readonly ItemRealizer _realizer;

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
            _realizer.RecycleAll();
            _realizer.ClearPool();

            field = value;
            _realizer.Invalidate();

            Invalidate();
        }
    }

    /// <summary>Past the end the last row stops at the top: a row, not a wheel step.</summary>
    protected override float ScrollPastEndRow => ItemHeight > 0 ? ItemHeight : base.ScrollPastEndRow;

    /// <summary>Row height. Equal for all — virtualization is built on that.</summary>
    public float ItemHeight
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // both the total size and the visible range depend on the row height
            _realizer.Invalidate();
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
            _realizer.Invalidate();
            Invalidate();
        }
    } = 3;

    public VirtualizingStackPanel()
    {
        // a pool of labels is useless to a template: only the panel's own labels are reused
        _realizer = new ItemRealizer(this, CreateContainer, _ => ItemTemplate is null);

        OverflowY = Overflow.Auto;
    }

    /// <summary>The contents or order of ItemsSource changed.</summary>
    public void Refresh()
    {
        // children are not touched here: clearing them now means showing an empty
        // list in the frame between Refresh and the next measure — that is
        // the flicker. It is enough to declare the range invalid,
        // and UpdateRealizedRange rebuilds it in the same layout pass
        _realizer.Invalidate();

        Invalidate();
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
    private bool UpdateRealizedRange(float viewportHeight) =>
        _realizer.Update(ItemsSource, ScrollY, viewportHeight, ItemHeight, OverscanCount);

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

        foreach (UIElement container in _realizer.Realized.Values)
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

            foreach (UIElement container in _realizer.Realized.Values)
                container.Measure(itemSize);
        }

        foreach ((int index, UIElement container) in _realizer.Realized)
        {
            container.Arrange(new Rectangle(
                new Point(Padding.Left, Padding.Top + index * ItemHeight),
                new Size(width, ItemHeight)));
        }
    }
}