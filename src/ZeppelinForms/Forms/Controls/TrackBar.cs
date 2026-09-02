using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class TrackBar : UnitControl, IInputElement
{
    private const float ThumbSize = 14f;
    private const float TrackThickness = 4f;

    private float _value;
    private bool _isDragging;

    public float Minimum { get; set; } = 0f;
    public float Maximum { get; set; } = 100f;
    public float Step { get; set; } = 1f;

    public float Value
    {
        get => _value;
        set
        {
            float clamped = Math.Clamp(value, Minimum, Maximum);
            if (Math.Abs(_value - clamped) < 0.001f) return;

            _value = clamped;
            ValueChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public event EventHandler? ValueChanged;

    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    public Color TrackColor { get; set; } = new Color(255, 210, 210, 210);
    public Color FillColor { get; set; } = new Color(255, 0x0D, 0x6E, 0xFD);
    public Color ThumbColor { get; set; } = Colors.White;
    public Color ThumbBorderColor { get; set; } = new Color(255, 130, 130, 130);

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    public TrackBar()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Center;
    }

    private float Fraction
    {
        get
        {
            float range = Maximum - Minimum;
            return range <= 0 ? 0 : Math.Clamp((_value - Minimum) / range, 0f, 1f);
        }
    }

    // длина, по которой реально ездит центр ползунка
    private float TravelLength => Math.Max(0,
        (Orientation == Orientation.Horizontal ? ActualSize.Width : ActualSize.Height) - ThumbSize);

    public override void Draw(Graphics g)
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
        g.DrawEllipse(thumb, IsFocused ? FillColor : ThumbBorderColor, 1.5f);
    }

    private void SetValueFromPoint(Point location)
    {
        Point abs = GetAbsolutePosition();
        float travel = TravelLength;
        if (travel <= 0) return;

        float fraction = Orientation == Orientation.Horizontal
            ? (location.X - abs.X - ThumbSize / 2f) / travel
            // вертикальный трек растёт вверх, поэтому инвертируем
            : 1f - (location.Y - abs.Y - ThumbSize / 2f) / travel;

        Value = Minimum + (Maximum - Minimum) * Math.Clamp(fraction, 0f, 1f);
    }

    protected override void OnMouseDown(MouseMoveEventArgs args)
    {
        _isDragging = true;
        SetValueFromPoint(args.Location);
    }

    protected override void OnMouseExit(MouseMoveEventArgs args)
    {
        if (_isDragging)
            SetValueFromPoint(args.Location);
    }

    protected override void OnMouseUp(MouseMoveEventArgs args) => _isDragging = false;

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
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