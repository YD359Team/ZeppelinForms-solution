using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls;

public partial class ScrollBar : DecoratedControl
{
    private const float MinThumbLength = 20f;

    private float _value;

    /// <summary>The mouse on the bar: the thumb dragged, the track paged and held.
    /// Made for the orientation at the first press, and made again when it changes.</summary>
    private ScrollBarInteraction? _input;

    public Orientation Orientation
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the interaction measures along the old axis: a press held now ends
            _input?.Release();
            _input = null;

            Invalidate();
        }
    } = Orientation.Vertical;

    /// <summary>The full size of the scrollable content.</summary>
    public float ContentSize
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            OnRangeChanged();
        }
    }

    /// <summary>The visible part of the content.</summary>
    public float ViewportSize
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            OnRangeChanged();
        }
    }

    public float MaxValue => Math.Max(0, ContentSize - ViewportSize);

    public float Value
    {
        get => _value;
        set
        {
            float clamped = Math.Clamp(value, 0, MaxValue);
            if (Math.Abs(_value - clamped) < 0.01f) return;

            _value = clamped;
            ValueChanged?.Invoke(this, EventArgs.Empty);

            // scrolling moves the thumb, not the geometry
            InvalidateVisual();
        }
    }

    public event EventHandler? ValueChanged;

    [Styled(Category = "Scrolling")]
    public partial Color TrackColor { get; set; }
    private static Color TrackColorDefault => new(255, 240, 240, 240);

    [Styled(Category = "Scrolling")]
    public partial Color ThumbColor { get; set; }
    private static Color ThumbColorDefault => new(255, 170, 170, 170);

    public ScrollBar() => Size = new Size(12, 12);

    private ScrollBarInteraction Input => _input ??= new ScrollBarInteraction(
        this,
        Orientation,
        () => LocalBounds,
        () => (ThumbPosition, ThumbLength),
        () => Value,
        () => MaxValue,
        () => ViewportSize,
        value => Value = value);

    /// <summary>The range changed: the value is brought back into it. Previously it
    /// stayed beyond the new limit when the content shrank, and the thumb was drawn
    /// past the end of the track.</summary>
    private void OnRangeChanged()
    {
        float clamped = Math.Clamp(_value, 0, MaxValue);

        if (clamped != _value)
        {
            _value = clamped;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
    }

    private float TrackLength => Orientation == Orientation.Vertical ? ActualSize.Height : ActualSize.Width;

    private float ThumbLength
    {
        get
        {
            if (ContentSize <= 0 || ViewportSize >= ContentSize) return TrackLength;
            return Math.Max(MinThumbLength, ViewportSize / ContentSize * TrackLength);
        }
    }

    private float ThumbPosition =>
        MaxValue <= 0 ? 0 : _value / MaxValue * (TrackLength - ThumbLength);

    public bool IsScrollable => ContentSize > ViewportSize;

    // the background, border and corner radius are drawn by the base — only the track and the thumb here
    protected override void DrawContent(Graphics g)
    {
        g.FillRectangle(this.LocalBounds, TrackColor);

        if (!IsScrollable) return;

        var thumb = Orientation == Orientation.Vertical
            ? new Rectangle(new Point(0, ThumbPosition), new Size(ActualSize.Width, ThumbLength))
            : new Rectangle(new Point(ThumbPosition, 0), new Size(ThumbLength, ActualSize.Height));

        g.FillRectangle(thumb, ThumbColor);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        // only the left button scrolls: the right one belongs to the context menu
        if (e.Button != MouseButton.Left) return;

        if (!IsScrollable) return;

        // a press on the thumb grabs it; one on the track pages at once
        // and keeps paging while held
        Input.Press(ToLocal(e.Location));
    }

    protected override void OnMouseMove(MouseMoveEventArgs e) => _input?.Move(ToLocal(e.Location));

    protected override void OnMouseUp(MouseButtonEventArgs e) => _input?.Release();

    // there will be no release after a cancel — reset ourselves
    protected override void OnPointerCanceled(PointerCancelEventArgs e) => _input?.Release();

    private Point ToLocal(Point location)
    {
        Point abs = GetAbsolutePosition();
        return new Point(location.X - abs.X, location.Y - abs.Y);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(new Size(12, 12), availableSize);
}