using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls;

public partial class TrackBar : InteractiveControl
{
    private const float ThumbSize = 14f;
    private const float TrackThickness = 4f;

    /// <summary>The value as it was assigned, before coercing into the range.</summary>
    /// <remarks>
    /// Coercing on read makes the result independent of the order of assignments:
    /// in <c>new TrackBar { Value = 150, Maximum = 200 }</c> the value used to be
    /// clamped by the default maximum of 100 before the real one arrived.
    /// </remarks>
    private float _requested;

    private bool _isDragging;

    /// <summary>The value before the press: a cancelled drag returns to it.</summary>
    private float _valueBeforeDrag;

    public float Minimum
    {
        get;
        set
        {
            if (field == value) return;

            float before = Value;
            field = value;
            OnRangeChanged(before);
        }
    } = 0f;

    public float Maximum
    {
        get;
        set
        {
            if (field == value) return;

            float before = Value;
            field = value;
            OnRangeChanged(before);
        }
    } = 100f;

    public float Step { get; set; } = 1f;

    public float Value
    {
        get => Coerce(_requested);
        set
        {
            float before = Value;
            _requested = value;

            if (Math.Abs(before - Value) < 0.001f) return;

            ValueChanged?.Invoke(this, EventArgs.Empty);

            // the value moves the thumb, not the geometry
            InvalidateVisual();
        }
    }

    // Min/Max rather than Math.Clamp: while the range is being reassigned,
    // Minimum may briefly exceed Maximum, and Math.Clamp throws on that
    private float Coerce(float value) => Math.Min(Math.Max(value, Minimum), Maximum);

    /// <summary>The range changed. If that moved the coerced value,
    /// whoever listens to ValueChanged must know it.</summary>
    private void OnRangeChanged(float before)
    {
        if (Math.Abs(before - Value) >= 0.001f)
            ValueChanged?.Invoke(this, EventArgs.Empty);

        InvalidateVisual();
    }

    public event EventHandler? ValueChanged;

    public Orientation Orientation
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the default size swaps its axes together with the orientation
            Invalidate();
        }
    } = Orientation.Horizontal;

    [Styled(Category = "Track")]
    public partial Color TrackColor { get; set; }
    private static Color TrackColorDefault => new(255, 210, 210, 210);

    [Styled(Category = "Track")]
    public partial Color FillColor { get; set; }
    private static Color FillColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    [Styled(Category = "Track")]
    public partial Color ThumbColor { get; set; }
    private static Color ThumbColorDefault => Colors.White;

    [Styled(Category = "Track")]
    public partial Color ThumbBorderColor { get; set; }
    private static Color ThumbBorderColorDefault => new(255, 130, 130, 130);

    public TrackBar()
    {
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        SetControlDefault(VerticalAlignmentProperty, VerticalAlignment.Center);
    }

    private float Fraction
    {
        get
        {
            float range = Maximum - Minimum;
            return range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0f, 1f);
        }
    }

    // the length the center of the thumb actually travels
    private float TravelLength => Math.Max(0,
        (Orientation == Orientation.Horizontal ? ActualSize.Width : ActualSize.Height) - ThumbSize);

    protected override void DrawContent(Graphics g)
    {
        var bounds = this.LocalBounds;
        float fraction = Fraction;

        if (Orientation == Orientation.Horizontal)
        {
            float trackY = bounds.Height / 2f - TrackThickness / 2f;
            var track = new Rectangle(new Point(ThumbSize / 2f, trackY),
                new Size(Math.Max(0, bounds.Width - ThumbSize), TrackThickness));

            g.FillRectangle(track, TrackColor);
            g.FillRectangle(new Rectangle(track.Position, new Size(track.Width * fraction, TrackThickness)), FillColor);

            float cx = ThumbSize / 2f + TravelLength * fraction;
            DrawThumb(g, cx, bounds.Height / 2f);
        }
        else
        {
            float trackX = bounds.Width / 2f - TrackThickness / 2f;
            var track = new Rectangle(new Point(trackX, ThumbSize / 2f),
                new Size(TrackThickness, Math.Max(0, bounds.Height - ThumbSize)));

            g.FillRectangle(track, TrackColor);

            float filled = track.Height * fraction;
            g.FillRectangle(
                new Rectangle(new Point(trackX, track.Y + track.Height - filled),
                    new Size(TrackThickness, filled)),
                FillColor);

            float cy = bounds.Height - ThumbSize / 2f - TravelLength * fraction;
            DrawThumb(g, bounds.Width / 2f, cy);
        }
    }

    private void DrawThumb(Graphics g, float cx, float cy)
    {
        var thumb = new Rectangle(
            new Point(cx - ThumbSize / 2f, cy - ThumbSize / 2f),
            new Size(ThumbSize, ThumbSize));

        g.FillEllipse(thumb, ThumbColor);

        // the thumb's accent outline is the track bar's focus mark — shown only
        // while focus is visible: under the mouse the thumb is visibly in hand
        g.DrawEllipse(thumb, IsFocusVisible ? FillColor : ThumbBorderColor, 1.5f);
    }

    private void SetValueFromPoint(Point location)
    {
        Point abs = GetAbsolutePosition();
        float travel = TravelLength;
        if (travel <= 0) return;

        float fraction = Orientation == Orientation.Horizontal
            ? (location.X - abs.X - ThumbSize / 2f) / travel
            // a vertical track grows upward, so invert
            : 1f - (location.Y - abs.Y - ThumbSize / 2f) / travel;

        Value = Minimum + (Maximum - Minimum) * Math.Clamp(fraction, 0f, 1f);
    }

    protected override void OnMouseDown(MouseButtonEventArgs args)
    {
        // only the left button drags: the right one belongs to the context menu
        if (args.Button != MouseButton.Left) return;

        _valueBeforeDrag = Value;
        _isDragging = true;

        // without capture the drag breaks off as soon as the cursor leaves the window
        CaptureMouse();

        SetValueFromPoint(args.Location);
    }

    protected override void OnMouseMove(MouseMoveEventArgs args)
    {
        if (_isDragging)
            SetValueFromPoint(args.Location);
    }

    protected override void OnMouseUp(MouseButtonEventArgs args)
    {
        if (!_isDragging) return;

        _isDragging = false;
        ReleaseMouseCapture();
    }

    /// <summary>The interaction was cut off. Previously _isDragging stayed true
    /// here, and after that simply hovering with no button pressed kept moving
    /// the thumb. Nothing was committed, so the value goes back to what it was
    /// before the press.</summary>
    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        if (!_isDragging) return;

        _isDragging = false;
        Value = _valueBeforeDrag;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // the form delivers the wheel to disabled elements too:
        // a disabled track bar must not move
        if (!IsEnabled) return;

        Value += Step * (e.Delta / 120f);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left or Key.Down: Value -= Step; e.Handled = true; break;
            case Key.Right or Key.Up: Value += Step; e.Handled = true; break;
            case Key.Home: Value = Minimum; e.Handled = true; break;
            case Key.End: Value = Maximum; e.Handled = true; break;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var content = Orientation == Orientation.Horizontal
            ? new Size(160 + Padding.Horizontal, ThumbSize + 6 + Padding.Vertical)
            : new Size(ThumbSize + 6 + Padding.Horizontal, 160 + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }
}