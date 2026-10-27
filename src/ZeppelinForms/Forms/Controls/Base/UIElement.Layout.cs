using ZeppelinForms.Diagnostics;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>Layout: measuring and arranging with the measure cache, and what invalidates
/// a measure or a drawing.</summary>
public abstract partial class UIElement
{
    private Size _measuredAgainst;
    private bool _measureValid;

    /// <summary>The measure cache. Turned off when you suspect an Invalidate
    /// is missing somewhere: comparing the behavior with and without
    /// the cache is the fastest way to confirm it.</summary>
    public static bool MeasureCacheEnabled { get; set; } = true;

    /// <summary>Check the cache instead of trusting it: the measurement is
    /// recomputed and compared with the stored one, and a mismatch is reported
    /// through ZfContract. A mismatch means the element's state changed without
    /// Invalidate. A debugging mode — twice as many measurements as without
    /// a cache at all.</summary>
    public static bool VerifyMeasureCache { get; set; }

    /// <remarks>
    /// Measuring again with the same constraint returns the same result —
    /// if the element's state hasn't changed. Whether it changed is told
    /// by Invalidate: it marks the element and all ancestors whose size
    /// is computed from it. Without this cache a full pass measured every
    /// element on every frame, although only a handful change per frame.
    ///
    /// The price is a requirement on controls: a property that affects size
    /// must call Invalidate. For [Styled] with AffectsLayout the property system
    /// does it itself, for ordinary properties — the property's author.
    /// </remarks>
    public void Measure(Size availableSize)
    {
        bool reusable = MeasureCacheEnabled
            && _measureValid
            && SameConstraint(_measuredAgainst, availableSize);

        if (reusable && !VerifyMeasureCache) return;

        Size measured = MeasureOverride(availableSize);

        if (reusable && measured != DesiredSize)
        {
            ZfContract.Fail(
                $"{GetType().Name} \"{Name}\": measuring gave {measured}, " +
                $"but the cache holds {DesiredSize}. So the element's state " +
                "changed without Invalidate — a property that affects size " +
                "must call it.");
        }

        DesiredSize = measured;
        _measuredAgainst = availableSize;
        _measureValid = true;
    }

    /// <summary>A layout property changed: request a layout pass for this element
    /// and, for a property its descendants take over, reset their measure too.</summary>
    /// <remarks>
    /// A descendant that inherits Font, TextTransform or FlowDirection is measured
    /// with the ancestor's value, but its own measure cache knows nothing about the
    /// ancestor: Invalidate marks only the element and its ancestors. A font set on
    /// a panel after the labels in it were measured left them at their old sizes.
    ///
    /// Font and FlowDirection are inherited through EffectiveFont and
    /// EffectiveFlowDirection rather than the Inherits flag, so they are named here.
    /// Descendants are marked first, before the request to the form: anything that
    /// lays out on that request — a platform painting inside Invalidate, as Win32
    /// and X11 once did — must find the marks already in place.
    /// </remarks>
    private void InvalidateLayoutFor(StyledProperty property)
    {
        // every way of setting a value comes here — the element's own, a style, a
        // theme, a binding — and visibility is a layout property
        if (ReferenceEquals(property, IsVisibleProperty))
            OnVisibilityChanged();

        if (property.Inherits ||
            ReferenceEquals(property, FontProperty) ||
            ReferenceEquals(property, FlowDirectionProperty))
            InvalidateDescendantsMeasure();

        Invalidate();
    }

    /// <summary>Reset the measure cache of all descendants, not touching this element.
    /// An element without children has nothing to reset.</summary>
    internal virtual void InvalidateDescendantsMeasure() { }

    /// <summary>Reset the measure cache of this element and its whole subtree.</summary>
    internal void InvalidateMeasureSubtree()
    {
        _measureValid = false;
        InvalidateDescendantsMeasure();
    }

    /// <summary>Infinities compare as equal, and so do NaNs:
    /// "no constraint" and "auto axis" must match themselves,
    /// otherwise the cache would never hit.</summary>
    private static bool SameConstraint(Size a, Size b) =>
        (a.Width == b.Width || (float.IsNaN(a.Width) && float.IsNaN(b.Width)))
        && (a.Height == b.Height || (float.IsNaN(a.Height) && float.IsNaN(b.Height)));

    private bool _hasBeenArranged;

    /// <summary>The size of the previous arrange. OnSizeChanged compares with it rather
    /// than with ActualSize: the Size setter writes ActualSize ahead of layout,
    /// and a comparison with that would miss a real change.</summary>
    private Size _arrangedSize;

    public void Arrange(Rectangle finalRect) =>
        // Dock.Fill is an explicit demand to take everything, it overrides alignment
        Arrange(finalRect, fill: Docking == Dock.Fill);

    /// <summary>Arrange, filling the rectangle whatever the alignment says when
    /// <paramref name="fill"/> is set: for a panel that gives a child the rest of the
    /// room by a rule of its own — a dock panel's last child — without writing
    /// <see cref="Docking"/>, which is the application's.</summary>
    /// <param name="finalRect">The slot the parent gives the element.</param>
    /// <param name="fill">Fill the axes left to the layout whatever the alignment says.</param>
    /// <param name="spanWidth">Take the slot's whole width even over an explicit width:
    /// a dock panel's child docked to the top or the bottom, as in WinForms.</param>
    /// <param name="spanHeight">The same for the height: a child docked to the left
    /// or the right.</param>
    internal void Arrange(Rectangle finalRect, bool fill, bool spanWidth = false, bool spanHeight = false)
    {
        // An explicit size beats stretching: Stretch and Dock.Fill fill only an axis
        // left to the layout. A progress bar of Size 100×10 in a wider slot used to be
        // stretched to the slot's width, its size quietly ignored; now it keeps its
        // size and stands in the middle of the slot, as in WPF and Avalonia
        //
        // A side dock is the exception, along the axis it runs: Dock.Top is a word
        // about the width as explicit as the Size, and a text box of Size 200×30 at
        // the top of a form used to stay 200 wide — the docking was the application's
        // wish too, and the one a WinForms author expects to win
        bool stretchH = spanWidth || (_explicitSize.IsWidthAuto && (fill || HorizontalAlignment == HorizontalAlignment.Stretch));
        bool stretchV = spanHeight || (_explicitSize.IsHeightAuto && (fill || VerticalAlignment == VerticalAlignment.Stretch));

        float width = stretchH ? finalRect.Width : Math.Min(DesiredSize.Width, finalRect.Width);
        float height = stretchV ? finalRect.Height : Math.Min(DesiredSize.Height, finalRect.Height);

        // a stretch that the explicit size refused centers the element, as Center does
        float x = stretchH ? finalRect.X : HorizontalAlignment switch
        {
            HorizontalAlignment.Right => finalRect.X + finalRect.Width - width,
            HorizontalAlignment.Center or HorizontalAlignment.Stretch => finalRect.X + (finalRect.Width - width) / 2f,
            _ => finalRect.X,
        };

        float y = stretchV ? finalRect.Y : VerticalAlignment switch
        {
            VerticalAlignment.Bottom => finalRect.Y + finalRect.Height - height,
            VerticalAlignment.Center or VerticalAlignment.Stretch => finalRect.Y + (finalRect.Height - height) / 2f,
            _ => finalRect.Y,
        };

        var placed = new Point(x, y);

        // the first arrange inside an already shown parent is an appearance:
        // the element was added to a screen that has already been seen.
        // A first arrange together with the parent is just the form opening,
        // there is no point animating it
        bool appearing = (!_hasBeenArranged && Parent is { _hasBeenArranged: true }) || _enterOnNextArrange;

        _enterOnNextArrange = false;

        // compare with the place from the previous layout, not with Position:
        // a scrolling panel adjusts its children's Position after Arrange,
        // and a move by the scroll amount must not be animated
        Point previous = _arrangedPosition;
        bool moved = _hasBeenArranged && (previous.X != placed.X || previous.Y != placed.Y);

        Position = placed;
        _arrangedPosition = placed;

        // the layout result goes into ActualSize; Size stays what the user set,
        // otherwise auto-sizing would work only once
        _actualSize = ArrangeOverride(new Size(width, height));

        if (moved && !_skipLayoutTransition) StartLayoutTransition(previous, placed);

        _skipLayoutTransition = false;

        if (appearing && !_skipEnterTransition) StartEnterTransition();

        _skipEnterTransition = false;

        // the first pass only records the size: derived classes that react
        // to a change must not fire on the transition out of "not laid out".
        // Later passes call it only on an actual change: Arrange runs on every
        // layout pass, and most of them leave the size as it was
        if (!_hasBeenArranged)
            _hasBeenArranged = true;
        else if (_actualSize != _arrangedSize)
            OnSizeChanged();

        _arrangedSize = _actualSize;
    }

    public void Arrange(Point point, Size size) => Arrange(new Rectangle(point, size));

    public Point GetAbsolutePosition()
    {
        float x = 0, y = 0;

        for (UIElement? current = this; current is not null; current = current.Parent)
        {
            x += current.Position.X;
            y += current.Position.Y;
        }

        return new Point(x, y);
    }

    // Default for leaf controls that didn't override MeasureOverride:
    // if Size is set explicitly, use it, otherwise (Auto) assume "I want 0".
    protected virtual Size MeasureOverride(Size availableSize) =>
        ResolveSize(Size.Empty, availableSize);

    // Default — simply fill everything the parent gave ("stretch").
    protected virtual Size ArrangeOverride(Size finalSize) => finalSize;

    /// <summary>Whether an explicit Size on this axis is set aside for now.
    /// A scrolling panel measures its content with it set aside: the explicit
    /// size on a scrolling axis is the size of the viewport, not of the content,
    /// and letting it through made the content report the viewport's size —
    /// there was nothing left to scroll.</summary>
    private protected virtual bool IsExplicitSizeSuspended(bool horizontal) => false;

    // Shared helper: an explicitly set Size axis beats contentSize,
    // an auto axis (NaN) takes the size computed from content, and neither
    // can exceed what the parent actually allotted.
    protected Size ResolveSize(Size contentSize, Size availableSize)
    {
        float w = _explicitSize.IsWidthAuto || IsExplicitSizeSuspended(horizontal: true)
            ? contentSize.Width
            : _explicitSize.Width;

        float h = _explicitSize.IsHeightAuto || IsExplicitSizeSuspended(horizontal: false)
            ? contentSize.Height
            : _explicitSize.Height;

        w = Math.Min(w, availableSize.Width);
        h = Math.Min(h, availableSize.Height);

        // infinity must not reach the sizes: it means "no constraint",
        // not "the element is infinite"
        return new Size(
            float.IsFinite(w) ? w : 0f,
            float.IsFinite(h) ? h : 0f);
    }

    /// <summary>Redraw only this element, without recomputing layout.</summary>
    protected internal void InvalidateVisual()
    {
        if (!float.IsFinite(ActualSize.Width) || !float.IsFinite(ActualSize.Height))
            return;

        FindOwner()?.InvalidateRect(DirtyBounds);
    }

    // protected internal — available both to derived classes (as before) and to
    // code inside the assembly such as FocusDispatcher, which needs to request
    // a redraw without being a UIElement subclass.
    /// <summary>The geometry changed — a full recompute and redraw are needed.</summary>
    protected internal void Invalidate()
    {
        // first our own measure cache and the ancestors' caches, then the request
        // to the form: without this the form would recompute the layout with stale sizes
        InvalidateMeasure();

        FindOwner()?.Invalidate();
    }

    /// <summary>Mark the measurement stale on the element and its ancestors:
    /// their size is computed from ours.</summary>
    /// <remarks>
    /// The walk up stops at the first element that is already marked: if an
    /// element is invalid, so are all its ancestors — they were marked together with it.
    /// <para>
    /// Not quite for the element itself, though: a hidden element is skipped by its
    /// panel's measure, so its mark stays while the panel's is cleared. The walk
    /// used to stop right at it, and an element shown again left its panel measured
    /// without it — the card of the show-and-hide demo didn't come back until
    /// something else laid the page out. The element's own mark no longer stops
    /// the walk; an ancestor's still does.
    /// </para>
    /// </remarks>
    internal void InvalidateMeasure()
    {
        _measureValid = false;

        for (UIElement? current = Parent; current is not null; current = current.Parent)
        {
            if (!current._measureValid) break;

            current._measureValid = false;
        }
    }
}
