using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls;

/// <summary>The thumbs of a <see cref="RangeSlider"/>.</summary>
public enum RangeThumb : byte
{
    Lower,
    Upper,
}

/// <summary>
/// A slider with two thumbs: a range from <see cref="LowerValue"/> to
/// <see cref="UpperValue"/> within <see cref="Minimum"/> and <see cref="Maximum"/> —
/// a price filter, a time window.
/// </summary>
/// <remarks>
/// <para>
/// The thumbs don't pass each other: a thumb pushed into the other one pushes it
/// along, the way a value assigned past the other one does. <see cref="MinimumGap"/>
/// keeps them apart.
/// </para>
/// <para>
/// The keyboard moves the active thumb — the one last pressed, the lower one at
/// first. Tab goes from the lower thumb to the upper one before it leaves the
/// slider, and Shift+Tab back: two stops, as two sliders would have. Focus that
/// comes back finds the thumb it left.
/// </para>
/// </remarks>
public partial class RangeSlider : InteractiveControl
{
    private const float ThumbSize = 14f;
    private const float TrackThickness = 4f;

    /// <summary>The values as they were assigned, coerced on read — the same reason
    /// as in <see cref="TrackBar"/>: the result doesn't depend on whether the range
    /// or the values come first in an initializer.</summary>
    private float _lowerRequested;
    private float _upperRequested = 100f;

    private RangeThumb? _dragging;

    /// <summary>Both thumbs stand on one spot: which of them the press takes is decided
    /// by the direction of the first move — otherwise a range closed at its minimum
    /// could never be opened again.</summary>
    private bool _undecided;

    private float _pressX;
    private float _lowerBeforeDrag;
    private float _upperBeforeDrag;

    public float Minimum
    {
        get;
        set
        {
            if (field == value) return;

            var before = (LowerValue, UpperValue);
            field = value;
            OnValuesChanged(before);
        }
    } = 0f;

    public float Maximum
    {
        get;
        set
        {
            if (field == value) return;

            var before = (LowerValue, UpperValue);
            field = value;
            OnValuesChanged(before);
        }
    } = 100f;

    /// <summary>What an arrow key moves a thumb by; the wheel moves by it too. Dragging
    /// snaps to it when <see cref="SnapToStep"/> is set.</summary>
    public float Step { get; set; } = 1f;

    /// <summary>Dragged values land on multiples of <see cref="Step"/> from <see cref="Minimum"/>.</summary>
    public bool SnapToStep { get; set; }

    /// <summary>How close the thumbs may come: 0 — up to the same value.</summary>
    public float MinimumGap
    {
        get;
        set
        {
            if (field == value) return;

            var before = (LowerValue, UpperValue);
            field = Math.Max(0, value);
            OnValuesChanged(before);
        }
    }

    /// <summary>The start of the range. Assigned past <see cref="UpperValue"/>, it pushes
    /// that along.</summary>
    public float LowerValue
    {
        get => Math.Min(Clamp(_lowerRequested), Math.Max(Minimum, Maximum - MinimumGap));
        set
        {
            var before = (LowerValue, UpperValue);

            _lowerRequested = value;

            if (Clamp(value) + MinimumGap > Clamp(_upperRequested))
                _upperRequested = Clamp(value) + MinimumGap;

            OnValuesChanged(before);
        }
    }

    /// <summary>The end of the range. Assigned below <see cref="LowerValue"/>, it pushes
    /// that back.</summary>
    public float UpperValue
    {
        get => Math.Max(Clamp(_upperRequested), LowerValue + Math.Min(MinimumGap, Maximum - LowerValue));
        set
        {
            var before = (LowerValue, UpperValue);

            _upperRequested = value;

            if (Clamp(value) - MinimumGap < Clamp(_lowerRequested))
                _lowerRequested = Clamp(value) - MinimumGap;

            OnValuesChanged(before);
        }
    }

    /// <summary>Set both ends at once — no pushing in between, one event.</summary>
    public void SetRange(float lower, float upper)
    {
        var before = (LowerValue, UpperValue);

        _lowerRequested = Math.Min(lower, upper);
        _upperRequested = Math.Max(lower, upper);

        OnValuesChanged(before);
    }

    /// <summary>The thumb the keyboard moves: the one last pressed.</summary>
    public RangeThumb ActiveThumb
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    }

    /// <summary>Either end moved.</summary>
    public event EventHandler? RangeChanged;

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

    [Styled(Category = "Track")]
    public partial Color TrackColor { get; set; }
    private static Color TrackColorDefault => new(255, 210, 210, 210);

    /// <summary>The track between the thumbs: the chosen range.</summary>
    [Styled(Category = "Track")]
    public partial Color FillColor { get; set; }
    private static Color FillColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    [Styled(Category = "Track")]
    public partial Color ThumbColor { get; set; }
    private static Color ThumbColorDefault => Colors.White;

    [Styled(Category = "Track")]
    public partial Color ThumbBorderColor { get; set; }
    private static Color ThumbBorderColorDefault => new(255, 130, 130, 130);

    public RangeSlider()
    {
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        SetControlDefault(VerticalAlignmentProperty, VerticalAlignment.Center);
    }

    // Min/Max rather than Math.Clamp: while the range is being reassigned,
    // Minimum may briefly exceed Maximum, and Math.Clamp throws on that
    private float Clamp(float value) => Math.Min(Math.Max(value, Minimum), Maximum);

    private void OnValuesChanged((float Lower, float Upper) before)
    {
        if (Math.Abs(before.Lower - LowerValue) < 0.001f && Math.Abs(before.Upper - UpperValue) < 0.001f)
        {
            InvalidateVisual();
            return;
        }

        RangeChanged?.Invoke(this, EventArgs.Empty);

        // the values move the thumbs, not the geometry
        InvalidateVisual();
    }

    // ===== geometry =====

    private bool IsHorizontal => Orientation == Orientation.Horizontal;

    /// <summary>The length the center of a thumb travels.</summary>
    private float TravelLength => Math.Max(0, (IsHorizontal ? ActualSize.Width : ActualSize.Height) - ThumbSize);

    private float FractionOf(float value)
    {
        float range = Maximum - Minimum;
        return range <= 0 ? 0 : Math.Clamp((value - Minimum) / range, 0f, 1f);
    }

    /// <summary>The center of a thumb along the track, in the element's own coordinates.
    /// A vertical slider grows upward, like <see cref="TrackBar"/>.</summary>
    private float PositionOf(float value)
    {
        float along = ThumbSize / 2f + TravelLength * FractionOf(value);

        return IsHorizontal ? along : ActualSize.Height - along;
    }

    private float ValueAt(Point location)
    {
        Point abs = GetAbsolutePosition();
        float travel = TravelLength;

        if (travel <= 0) return Minimum;

        float fraction = IsHorizontal
            ? (location.X - abs.X - ThumbSize / 2f) / travel
            : 1f - (location.Y - abs.Y - ThumbSize / 2f) / travel;

        float value = Minimum + (Maximum - Minimum) * Math.Clamp(fraction, 0f, 1f);

        if (SnapToStep && Step > 0)
            value = Clamp(Minimum + MathF.Round((value - Minimum) / Step) * Step);

        return value;
    }

    // ===== drawing =====

    protected override void DrawContent(Graphics g)
    {
        Rectangle bounds = LocalBounds;
        float lower = PositionOf(LowerValue);
        float upper = PositionOf(UpperValue);

        if (IsHorizontal)
        {
            float y = bounds.Height / 2f - TrackThickness / 2f;

            g.FillRectangle(
                new Rectangle(new Point(ThumbSize / 2f, y), new Size(Math.Max(0, bounds.Width - ThumbSize), TrackThickness)),
                TrackColor);

            g.FillRectangle(new Rectangle(new Point(lower, y), new Size(Math.Max(0, upper - lower), TrackThickness)), FillColor);

            DrawThumb(g, lower, bounds.Height / 2f, RangeThumb.Lower);
            DrawThumb(g, upper, bounds.Height / 2f, RangeThumb.Upper);
        }
        else
        {
            float x = bounds.Width / 2f - TrackThickness / 2f;

            g.FillRectangle(
                new Rectangle(new Point(x, ThumbSize / 2f), new Size(TrackThickness, Math.Max(0, bounds.Height - ThumbSize))),
                TrackColor);

            // upward: the upper value is the higher point, with the smaller y
            g.FillRectangle(new Rectangle(new Point(x, upper), new Size(TrackThickness, Math.Max(0, lower - upper))), FillColor);

            DrawThumb(g, bounds.Width / 2f, lower, RangeThumb.Lower);
            DrawThumb(g, bounds.Width / 2f, upper, RangeThumb.Upper);
        }
    }

    private void DrawThumb(Graphics g, float cx, float cy, RangeThumb thumb)
    {
        var rect = new Rectangle(new Point(cx - ThumbSize / 2f, cy - ThumbSize / 2f), new Size(ThumbSize, ThumbSize));

        g.FillEllipse(rect, ThumbColor);

        // the accent outline marks the thumb the keyboard moves, while focus is visible
        bool marked = IsFocusVisible && ActiveThumb == thumb;
        g.DrawEllipse(rect, marked ? FillColor : ThumbBorderColor, marked ? 2f : 1.5f);
    }

    // ===== mouse =====

    protected override void OnMouseDown(MouseButtonEventArgs args)
    {
        // only the left button drags: the right one belongs to the context menu
        if (args.Button != MouseButton.Left) return;

        float value = ValueAt(args.Location);

        _lowerBeforeDrag = LowerValue;
        _upperBeforeDrag = UpperValue;
        _pressX = IsHorizontal ? args.Location.X : args.Location.Y;

        float toLower = Math.Abs(value - LowerValue);
        float toUpper = Math.Abs(value - UpperValue);

        _undecided = Math.Abs(LowerValue - UpperValue) < 0.001f && Math.Abs(toLower - toUpper) < 0.001f;

        RangeThumb thumb = toLower < toUpper || (toLower == toUpper && value < LowerValue)
            ? RangeThumb.Lower
            : RangeThumb.Upper;

        _dragging = thumb;
        ActiveThumb = thumb;

        // without capture the drag breaks off as soon as the cursor leaves the window
        CaptureMouse();

        // a press on the track beside a thumb moves the nearest one there
        if (!_undecided) MoveThumb(thumb, value);
    }

    protected override void OnMouseMove(MouseMoveEventArgs args)
    {
        if (_dragging is not { } thumb) return;

        if (_undecided)
        {
            float along = IsHorizontal ? args.Location.X : args.Location.Y;
            float moved = along - _pressX;

            if (Math.Abs(moved) < 1f) return;

            // the direction picks the thumb: toward the larger values — the upper one.
            // A vertical slider grows upward, against the screen's y
            bool towardLarger = IsHorizontal ? moved > 0 : moved < 0;

            thumb = towardLarger ? RangeThumb.Upper : RangeThumb.Lower;
            _dragging = thumb;
            ActiveThumb = thumb;
            _undecided = false;
        }

        MoveThumb(thumb, ValueAt(args.Location));
    }

    private void MoveThumb(RangeThumb thumb, float value)
    {
        // a dragged thumb pushes the other one, as an assignment does
        if (thumb == RangeThumb.Lower) LowerValue = value;
        else UpperValue = value;
    }

    protected override void OnMouseUp(MouseButtonEventArgs args)
    {
        if (_dragging is null) return;

        _dragging = null;
        _undecided = false;
        ReleaseMouseCapture();
    }

    /// <summary>The interaction was cut off: nothing was committed, the range goes
    /// back to what it was before the press.</summary>
    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        if (_dragging is null) return;

        _dragging = null;
        _undecided = false;
        SetRange(_lowerBeforeDrag, _upperBeforeDrag);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // the form delivers the wheel to disabled elements too
        if (!IsEnabled) return;

        Nudge(Step * (e.Delta / 120f));
        e.Handled = true;
    }

    // ===== keyboard =====

    private void Nudge(float delta)
    {
        if (ActiveThumb == RangeThumb.Lower) LowerValue += delta;
        else UpperValue += delta;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool shift = e.Modifiers.HasFlag(KeyModifiers.Shift);

        switch (e.Key)
        {
            case Key.Left or Key.Down:
                Nudge(-Step);
                break;

            case Key.Right or Key.Up:
                Nudge(Step);
                break;

            case Key.PageDown:
                Nudge(-Step * 10);
                break;

            case Key.PageUp:
                Nudge(Step * 10);
                break;

            case Key.Home:
                if (ActiveThumb == RangeThumb.Lower) LowerValue = Minimum;
                else UpperValue = LowerValue + MinimumGap;
                break;

            case Key.End:
                if (ActiveThumb == RangeThumb.Upper) UpperValue = Maximum;
                else LowerValue = UpperValue - MinimumGap;
                break;

            // the second thumb is the second tab stop
            case Key.Tab when !shift && ActiveThumb == RangeThumb.Lower:
                ActiveThumb = RangeThumb.Upper;
                break;

            case Key.Tab when shift && ActiveThumb == RangeThumb.Upper:
                ActiveThumb = RangeThumb.Lower;
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var content = IsHorizontal
            ? new Size(160 + Padding.Horizontal, ThumbSize + 6 + Padding.Vertical)
            : new Size(ThumbSize + 6 + Padding.Horizontal, 160 + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }

    // ===== accessibility =====

    /// <summary>A thumb's band in the form's coordinates, for its accessibility peer.</summary>
    internal Rectangle ThumbBounds(RangeThumb thumb)
    {
        float position = PositionOf(thumb == RangeThumb.Lower ? LowerValue : UpperValue);
        Point origin = GetAbsolutePosition();

        Point center = IsHorizontal
            ? new Point(position, ActualSize.Height / 2f)
            : new Point(ActualSize.Width / 2f, position);

        return new Rectangle(
            new Point(origin.X + center.X - ThumbSize / 2f, origin.Y + center.Y - ThumbSize / 2f),
            new Size(ThumbSize, ThumbSize));
    }
}