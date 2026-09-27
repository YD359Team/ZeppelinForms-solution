using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Two areas with a draggable splitter between them.
/// </summary>
public partial class SplitContainer : DecoratedPanel
{
    private UIElement? _first;
    private UIElement? _second;

    private bool _dragging;
    private float _dragOffset;
    private bool _splitterHovered;

    // the splitter as it was before the press: a cancelled drag returns to it
    private float _positionBeforeDrag;
    private float _ratioBeforeDrag;

    // backing fields rather than auto-properties: layout adapts them
    // directly, without the Invalidate the public setters call
    private float _splitterPosition = -1f;
    private float _splitterRatio = 0.5f;

    /// <summary>The extent the stored position and ratio describe. -1 — nothing
    /// has been laid out yet.</summary>
    private float _adaptedExtent = -1f;

    public Orientation Orientation
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = Orientation.Horizontal;

    public float SplitterThickness
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = 6f;

    /// <summary>The splitter's position from the start. Negative — compute from SplitterRatio.</summary>
    public float SplitterPosition
    {
        get => _splitterPosition;
        set
        {
            if (_splitterPosition == value) return;

            _splitterPosition = value;
            Invalidate();
        }
    }

    /// <summary>The share of the first area, if the position is not set explicitly.</summary>
    public float SplitterRatio
    {
        get => _splitterRatio;
        set
        {
            if (_splitterRatio == value) return;

            _splitterRatio = value;
            Invalidate();
        }
    }

    public float FirstMinSize
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = 40f;

    public float SecondMinSize
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = 40f;

    /// <summary>The panel that keeps its size when the container changes.</summary>
    public SplitterFixedPanel FixedPanel { get; set; } = SplitterFixedPanel.None;

    [Styled(Category = "Splitter")]
    public partial Color SplitterColor { get; set; }
    private static Color SplitterColorDefault => new(255, 224, 224, 224);

    [Styled(Category = "Splitter")]
    public partial Color SplitterHoverColor { get; set; }
    private static Color SplitterHoverColorDefault => new(255, 190, 190, 190);

    public event EventHandler? SplitterMoved;

    public UIElement? First
    {
        get => _first;
        set => Replace(ref _first, value, 0);
    }

    public UIElement? Second
    {
        get => _second;
        set => Replace(ref _second, value, 1);
    }

    private void Replace(ref UIElement? field, UIElement? value, int slot)
    {
        if (ReferenceEquals(field, value)) return;

        if (field is not null)
            Children.Remove(field);

        field = value;

        if (value is null) return;

        // the order in Children defines the slot: 0 — the first area, 1 — the second
        int index = slot == 0 ? 0 : Children.Count;
        Children.Insert(Math.Min(index, Children.Count), value);
    }

    private bool IsHorizontal => Orientation == Orientation.Horizontal;

    private float TotalExtent => IsHorizontal ? ContentBounds.Width : ContentBounds.Height;

    /// <summary>The splitter position for the given extent, within the minimum sizes
    /// of both areas. One place for measuring, arranging and hit testing.</summary>
    private float ClampPosition(float total)
    {
        float position = _splitterPosition >= 0 ? _splitterPosition : total * _splitterRatio;

        float max = Math.Max(FirstMinSize, total - SecondMinSize - SplitterThickness);

        return Math.Clamp(position, FirstMinSize, max);
    }

    private float ResolvedPosition => ClampPosition(TotalExtent);

    private Rectangle SplitterRect
    {
        get
        {
            Rectangle content = ContentBounds;
            float position = ResolvedPosition;

            return IsHorizontal
                ? new Rectangle(new Point(content.X + position, content.Y), new Size(SplitterThickness, content.Height))
                : new Rectangle(new Point(content.X, content.Y + position), new Size(content.Width, SplitterThickness));
        }
    }

    // the background, border and corner radius are drawn by the base — only the splitter itself here
    protected override void DrawContent(Graphics g)
    {
        g.FillRectangle(SplitterRect, _splitterHovered || _dragging ? SplitterHoverColor : SplitterColor);
    }

    // ===== input =====

    protected internal override bool HitTestSelfFirst(Point localPoint)
    {
        // the splitter belongs to the container, not to the areas under it
        Rectangle rect = SplitterRect;

        return localPoint.X >= rect.X && localPoint.X <= rect.X + rect.Width
            && localPoint.Y >= rect.Y && localPoint.Y <= rect.Y + rect.Height;
    }

    protected override void OnMouseMove(MouseMoveEventArgs args)
    {
        Point abs = GetAbsolutePosition();

        if (_dragging)
        {
            float position = IsHorizontal
                ? args.Location.X - abs.X - ContentBounds.X - _dragOffset
                : args.Location.Y - abs.Y - ContentBounds.Y - _dragOffset;

            // the setter requests the layout pass
            SplitterPosition = position;
            SplitterMoved?.Invoke(this, EventArgs.Empty);
            return;
        }

        var local = new Point(args.Location.X - abs.X, args.Location.Y - abs.Y);
        bool hovered = HitTestSelfFirst(local);

        if (hovered == _splitterHovered) return;

        _splitterHovered = hovered;
        Cursor = hovered
            ? (IsHorizontal ? CursorKind.SizeWestEast : CursorKind.SizeNorthSouth)
            : CursorKind.Default;

        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs args)
    {
        // only the left button drags: the right one belongs to the context menu
        if (args.Button != MouseButton.Left) return;

        Point abs = GetAbsolutePosition();
        var local = new Point(args.Location.X - abs.X, args.Location.Y - abs.Y);

        if (!HitTestSelfFirst(local)) return;

        _dragging = true;

        _positionBeforeDrag = _splitterPosition;
        _ratioBeforeDrag = _splitterRatio;

        // remember which point of the splitter was grabbed,
        // otherwise it jumps under the cursor on the first move
        _dragOffset = IsHorizontal
            ? local.X - SplitterRect.X
            : local.Y - SplitterRect.Y;

        // without capture the drag breaks off as soon as the cursor leaves the window
        CaptureMouse();
    }

    protected override void OnMouseUp(MouseButtonEventArgs args)
    {
        if (!_dragging) return;

        _dragging = false;

        ReleaseMouseCapture();
        InvalidateVisual();
    }

    /// <summary>The interaction was cut off: nothing was committed, so the splitter
    /// goes back to where it was before the press.</summary>
    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;

        SplitterPosition = _positionBeforeDrag;
        SplitterRatio = _ratioBeforeDrag;

        SplitterMoved?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (!_splitterHovered) return;

        _splitterHovered = false;
        Cursor = CursorKind.Default;

        // without a redraw the splitter stayed highlighted after the mouse left
        InvalidateVisual();
    }

    // ===== layout =====

    /// <summary>Bring the stored position to a new extent of the container.</summary>
    /// <remarks>
    /// When the container changes, the fixed panel keeps its size and the other one
    /// takes the difference; without a fixed panel the proportion is kept.
    ///
    /// This used to live in OnSizeChanged and relied on it being called on every
    /// layout pass. Besides, FixedPanel.Second never worked: the second area's size
    /// it was supposed to keep was never recorded, and the area shrank to its
    /// minimum on every resize. FixedPanel.First kept the size only after the first
    /// drag. Here the position is recomputed from the extent it was set for,
    /// in every mode, and without any Invalidate — this runs inside layout.
    /// </remarks>
    private void AdaptToExtent(float extent)
    {
        if (!float.IsFinite(extent)) return;

        float previous = _adaptedExtent;
        _adaptedExtent = extent;

        if (previous <= 0 || previous == extent) return;

        float position = _splitterPosition >= 0 ? _splitterPosition : previous * _splitterRatio;

        switch (FixedPanel)
        {
            case SplitterFixedPanel.First:
                _splitterPosition = position;
                break;

            case SplitterFixedPanel.Second:
                // the second area's extent plus the splitter stays the same
                _splitterPosition = extent - (previous - position);
                break;

            default:
                _splitterRatio = position / previous;
                _splitterPosition = -1f;
                break;
        }
    }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        float extent = GetExtent(inner);

        AdaptToExtent(extent);

        if (float.IsFinite(extent))
        {
            float position = ClampPosition(extent);
            float rest = Math.Max(0, extent - position - SplitterThickness);

            _first?.Measure(WithExtent(inner, position));
            _second?.Measure(WithExtent(inner, rest));
        }
        else
        {
            // along an unbounded axis there is nothing to split: each area
            // gets as much as it asks for. Previously the position came out
            // infinite here, and the second area was measured with NaN
            _first?.Measure(inner);
            _second?.Measure(inner);
        }

        Size first = _first?.DesiredSize ?? Size.Empty;
        Size second = _second?.DesiredSize ?? Size.Empty;

        Size content = IsHorizontal
            ? new Size(first.Width + SplitterThickness + second.Width, Math.Max(first.Height, second.Height))
            : new Size(Math.Max(first.Width, second.Width), first.Height + SplitterThickness + second.Height);

        // a split container fills what it is given; only along an unbounded axis
        // does it fall back to what its areas need. Padding is part of the size:
        // it used to be left out, and the container asked for less than it took
        return ResolveSize(
            new Size(
                (float.IsFinite(inner.Width) ? inner.Width : content.Width) + Padding.Horizontal,
                (float.IsFinite(inner.Height) ? inner.Height : content.Height) + Padding.Vertical),
            availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        var area = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, contentSize.Width - Padding.Horizontal),
                Math.Max(0, contentSize.Height - Padding.Vertical)));

        float total = GetExtent(area.Size);

        // usually already done in measuring; here for an arrange
        // that came with a size measuring didn't see
        AdaptToExtent(total);

        float position = ClampPosition(total);
        float rest = Math.Max(0, total - position - SplitterThickness);

        if (IsHorizontal)
        {
            _first?.Arrange(new Rectangle(area.Position, new Size(position, area.Height)));

            _second?.Arrange(new Rectangle(
                new Point(area.X + position + SplitterThickness, area.Y),
                new Size(rest, area.Height)));
        }
        else
        {
            _first?.Arrange(new Rectangle(area.Position, new Size(area.Width, position)));

            _second?.Arrange(new Rectangle(
                new Point(area.X, area.Y + position + SplitterThickness),
                new Size(area.Width, rest)));
        }
    }

    private float GetExtent(Size size) => IsHorizontal ? size.Width : size.Height;

    private Size WithExtent(Size size, float extent) =>
        IsHorizontal ? new Size(extent, size.Height) : new Size(size.Width, extent);
}

public enum SplitterFixedPanel { None, First, Second }