using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class ScrollBar : UnitControl
{
    private const float MinThumbLength = 20f;

    private bool _isDragging;
    private float _dragOffset;
    private float _value;

    public Orientation Orientation { get; set; } = Orientation.Vertical;

    /// <summary>Полный размер прокручиваемого содержимого.</summary>
    public float ContentSize { get; set; }

    /// <summary>Видимая часть содержимого.</summary>
    public float ViewportSize { get; set; }

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
            Invalidate();
        }
    }

    public event EventHandler? ValueChanged;

    public Color TrackColor { get; set; } = new Color(255, 240, 240, 240);
    public Color ThumbColor { get; set; } = new Color(255, 170, 170, 170);

    public ScrollBar() => Size = new Size(12, 12);

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

    public override void Draw(Graphics g)
    {
        g.FillRectangle(this.LocalBounds, TrackColor);

        if (!IsScrollable) return;

        var thumb = Orientation == Orientation.Vertical
            ? new Rectangle(new Point(0, ThumbPosition), new Size(ActualSize.Width, ThumbLength))
            : new Rectangle(new Point(ThumbPosition, 0), new Size(ThumbLength, ActualSize.Height));

        g.FillRectangle(thumb, ThumbColor);
    }

    protected override void OnMouseDown(MouseButtonEventArgs args)
    {
        if (!IsScrollable) return;

        Point abs = GetAbsolutePosition();
        float local = Orientation == Orientation.Vertical
            ? args.Location.Y - abs.Y
            : args.Location.X - abs.X;

        float thumbPos = ThumbPosition;

        if (local >= thumbPos && local <= thumbPos + ThumbLength)
        {
            _isDragging = true;
            _dragOffset = local - thumbPos;   // тянем за ту точку, где схватили
        }
        else
        {
            // клик по дорожке — страница вверх/вниз
            Value += local < thumbPos ? -ViewportSize : ViewportSize;
        }
    }

    protected override void OnMouseExit(MouseMoveEventArgs args)
    {
        if (!_isDragging) return;

        Point abs = GetAbsolutePosition();
        float local = Orientation == Orientation.Vertical
            ? args.Location.Y - abs.Y
            : args.Location.X - abs.X;

        float free = TrackLength - ThumbLength;
        if (free <= 0) return;

        Value = (local - _dragOffset) / free * MaxValue;
    }

    protected override void OnMouseUp(MouseButtonEventArgs location) => _isDragging = false;

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(new Size(12, 12), availableSize);
}