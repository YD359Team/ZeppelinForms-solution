using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Allow zoom in\out for child
/// </summary>
/// <remarks>
/// <para>
/// The wheel and a pinch zoom around the point under the cursor or between the
/// fingers, <see cref="Zoom(float)"/> around the middle of the box. Zoomed in, the
/// child is dragged with the mouse or a finger to see the rest of it.
/// </para>
/// <para>
/// Before 0.15 the child was scaled around the box's top-left corner: a child in the
/// middle of the box slid out of view to the right and down as it grew, and the box
/// showed mostly its own background. There was no way to move it back either.
/// </para>
/// </remarks>
public class ZoomBox : DecoratedWrapControl, IInputElement
{
    /// <summary>Where the zoomed child is moved to: added after scaling, in the box's
    /// own coordinates. Kept within <see cref="ClampPan"/> — the child never leaves
    /// the box entirely.</summary>
    private Point _pan;

    private Point _panAtStart;
    private float _zoomAtStart = 1f;

    public float ZoomStep { get; set; } = 0.1f;
    public float MinZoom { get; set; } = 0.1f;
    public float MaxZoom { get; set; } = 10f;
    public float ZoomFactor { get; private set; } = 1f;

    /// <summary>The keys to hold for the wheel to zoom. None — the wheel always zooms;
    /// <see cref="KeyModifiers.Control"/> leaves the plain wheel to the page around the
    /// box, which goes on scrolling over it.</summary>
    public KeyModifiers WheelZoomModifiers { get; set; } = KeyModifiers.None;

    /// <summary>The zoom factor changed: by the wheel, a pinch or a call.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>How far the zoomed child is moved, in the box's coordinates: zero
    /// until it is zoomed around anything but the top-left corner or dragged.</summary>
    public Point PanOffset => ClampPan(_pan, ZoomFactor);

    // IInputElement
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; }
    public uint TabIndex { get; set; }

    public ZoomBox()
    {
        AttachGestures();
    }

    public ZoomBox(UIElement child) : base(child)
    {
        AttachGestures();
    }

    /// <summary>Zoom around the middle of the box: what is in the middle stays there.</summary>
    public void Zoom(float factor)
    {
        Rectangle view = ContentBounds;

        Zoom(factor, new Point(view.X + view.Width / 2f, view.Y + view.Height / 2f));
    }

    /// <summary>Zoom around a point of the box, in its own coordinates: what is under
    /// the point stays under it.</summary>
    public void Zoom(float factor, Point anchor)
    {
        float target = Math.Clamp(factor, MinZoom, MaxZoom);
        float current = ZoomFactor;
        Point pan = PanOffset;

        // the child's point under the anchor, before and after: anchor = pan + zoom * point
        float ratio = target / current;
        var moved = new Point(
            anchor.X - (anchor.X - pan.X) * ratio,
            anchor.Y - (anchor.Y - pan.Y) * ratio);

        ZoomFactor = target;
        _pan = ClampPan(moved, target);

        InvalidateVisual();

        if (target != current) ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Back to the child as it is laid out: no zoom, no move.</summary>
    public void ResetZoom()
    {
        float current = ZoomFactor;

        ZoomFactor = Math.Clamp(1f, MinZoom, MaxZoom);
        _pan = ClampPan(Point.Empty, ZoomFactor);

        InvalidateVisual();

        if (ZoomFactor != current) ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // without the keys the wheel goes on to the page around the box
        if ((Keyboard.Modifiers & WheelZoomModifiers) != WheelZoomModifiers) return;

        float steps = e.Delta / 120f;
        Zoom(ZoomFactor * MathF.Pow(1f + ZoomStep, steps), ToLocal(e.Location));
        e.Handled = true;
    }

    protected internal override bool TransformsChild => true;

    /// <summary>Zoomed in, the child is larger than the box: it is cut off inside
    /// the padding, as scrolled content is, and doesn't spill over the frame.</summary>
    protected internal override Rectangle ClipBounds => ContentBounds;

    protected internal override void ApplyChildTransform(Graphics g)
    {
        // the pan is clamped here too, not only when it changes: the box may have
        // been laid out anew since — resized, or given a child of another size
        Point pan = PanOffset;

        g.Translate(pan.X, pan.Y);
        g.Scale(ZoomFactor, ZoomFactor);
    }

    protected internal override Point TransformPointToChild(Point point)
    {
        Point pan = PanOffset;

        return new((point.X - pan.X) / ZoomFactor, (point.Y - pan.Y) / ZoomFactor);
    }

    /// <summary>The child is dragged only when it doesn't fit: otherwise a press on it
    /// is left to the child and to the page around, untouched.</summary>
    private bool CanPan
    {
        get
        {
            if (Child is not { } child) return false;

            Rectangle view = ContentBounds;

            return child.ActualSize.Width * ZoomFactor > view.Width + 0.5f ||
                   child.ActualSize.Height * ZoomFactor > view.Height + 0.5f;
        }
    }

    private void AttachGestures()
    {
        PanGestureRecognizer pan = AddGesture(new PanGestureRecognizer { CanBegin = _ => IsEnabled && CanPan });
        pan.Started += (_, _) => _panAtStart = PanOffset;
        pan.Updated += (_, e) =>
        {
            _pan = ClampPan(new Point(_panAtStart.X + e.TotalOffset.X, _panAtStart.Y + e.TotalOffset.Y), ZoomFactor);
            InvalidateVisual();
        };

        PinchGestureRecognizer pinch = AddGesture(new PinchGestureRecognizer());
        pinch.Started += (_, _) => _zoomAtStart = ZoomFactor;
        pinch.Updated += (_, e) =>
        {
            if (IsEnabled) Zoom(_zoomAtStart * e.Scale, ToLocal(e.Center));
        };
    }

    /// <summary>Keep the zoomed child in sight. Larger than the box on an axis, it
    /// covers the box on that axis — no empty strip opens at an edge; smaller, it
    /// stays wholly inside. Not zoomed and not moved, the child is where the layout
    /// put it, and that satisfies both.</summary>
    private Point ClampPan(Point pan, float zoom)
    {
        if (Child is not { } child) return pan;

        Rectangle view = ContentBounds;

        return new Point(
            ClampAxis(pan.X, child.Position.X * zoom, child.ActualSize.Width * zoom, view.X, view.Width),
            ClampAxis(pan.Y, child.Position.Y * zoom, child.ActualSize.Height * zoom, view.Y, view.Height));
    }

    /// <param name="pan">The offset to clamp.</param>
    /// <param name="start">Where the scaled child starts with no offset.</param>
    /// <param name="length">The scaled child's length.</param>
    /// <param name="viewStart">Where the box's content area starts.</param>
    /// <param name="viewLength">Its length.</param>
    private static float ClampAxis(float pan, float start, float length, float viewStart, float viewLength)
    {
        float viewEnd = viewStart + viewLength;

        // larger: its start no further than the box's start, its end no nearer than
        // the box's end; smaller: the other way round
        float a = viewStart - start;
        float b = viewEnd - (start + length);

        return Math.Clamp(pan, Math.Min(a, b), Math.Max(a, b));
    }

    /// <summary>A window point in the box's coordinates: pointer events come in the window's.</summary>
    private Point ToLocal(Point location)
    {
        Point absolute = GetAbsolutePosition();

        return new Point(location.X - absolute.X, location.Y - absolute.Y);
    }
}
