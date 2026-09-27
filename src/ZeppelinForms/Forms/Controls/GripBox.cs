using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// A wrapper with handles around its content: dragging changes the child's size
/// and rotation angle, as in a visual editor.
/// The size is written to the child's <see cref="UIElement.Size"/>, the angle to
/// <see cref="UIElement.Rotation"/>; both are already understood by the renderer
/// and hit testing, GripBox sets up no transforms of its own.
/// </summary>
public partial class GripBox : DecoratedWrapControl
{
    private static readonly GripKind[] ResizeGrips =
    [
        GripKind.TopLeft, GripKind.Top, GripKind.TopRight, GripKind.Right,
        GripKind.BottomRight, GripKind.Bottom, GripKind.BottomLeft, GripKind.Left,
    ];

    private GripKind _active = GripKind.None;
    private GripKind _hovered = GripKind.None;

    private Point _dragStart;
    private Size _startSize;
    private Thickness _startMargin;
    private float _startRotation;
    private float _startAngle;

    /// <summary>The child's Size as it was set before the press — possibly Auto.
    /// A cancelled resize restores this rather than _startSize: the latter is the
    /// actual size, and restoring it would turn an auto-sized child into
    /// a fixed one.</summary>
    private Size _startExplicitSize;

    public float HandleSize { get; set; } = 8f;

    /// <summary>How far the rotation handle sticks out above the top edge.</summary>
    public float RotateHandleOffset { get; set; } = 24f;

    [Styled]
    public partial Color HandleColor { get; set; }
    [Styled]
    public partial Color HandleBorderColor { get; set; }
    [Styled]
    public partial Color OutlineColor { get; set; }

    public float OutlineWidth { get; set; } = 1f;

    public bool AllowResize { get; set; } = true;
    public bool AllowRotate { get; set; } = true;

    public Size MinChildSize { get; set; } = new(16f, 16f);

    /// <summary>Angle snapping step in degrees. 0 — rotation without snapping.</summary>
    public float RotationSnap { get; set; }

    /// <summary>
    /// Dragging the left or top handle keeps the opposite edge in place.
    /// Works through the GripBox's own <see cref="UIElement.Margin"/>, so it is
    /// meaningful only in a layout where the margin actually moves the element,
    /// and only at zero rotation.
    /// </summary>
    public bool AnchorOppositeEdge { get; set; } = true;

    public event EventHandler? ChildResized;
    public event EventHandler? ChildRotated;

    public GripBox()
    {
        ReserveGutter();
    }

    public GripBox(UIElement child) : base(child)
    {
        ReserveGutter();
    }

    /// <summary>Padding for the handles, so that they don't overlap the content.
    /// Call it again if <see cref="HandleSize"/> was changed.</summary>
    public void ReserveGutter()
    {
        float gutter = HandleSize;

        Padding = new Thickness(
            gutter,
            gutter + (AllowRotate ? RotateHandleOffset : 0f),
            gutter,
            gutter);
    }

    /// <summary>The child's rectangle in GripBox coordinates.</summary>
    private Rectangle ChildBounds => Child is null
        ? Rectangle.Empty
        : new Rectangle(Child.Position, Child.ActualSize);

    private float ChildRotation => Child?.Rotation ?? 0f;

    private float HitRadius => HandleSize / 2f + 2f;

    protected override void DrawDecoration(Graphics g)
    {
        if (Child is null) return;

        Rectangle bounds = ChildBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        DrawOutline(g, bounds);

        if (AllowRotate)
        {
            Point top = HandleCenter(GripKind.Top, bounds);
            Point knob = HandleCenter(GripKind.Rotate, bounds);

            g.DrawLine(top, knob, OutlineColor, OutlineWidth);

            float radius = HandleSize / 2f;
            var circle = new Rectangle(
                new Point(knob.X - radius, knob.Y - radius),
                new Size(HandleSize, HandleSize));

            g.FillEllipse(circle, HandleColor);
            g.DrawEllipse(circle, HandleBorderColor, 1f);
        }

        if (!AllowResize) return;

        foreach (GripKind grip in ResizeGrips)
        {
            Rectangle square = HandleRect(grip, bounds);

            g.FillRectangle(square, HandleColor);
            g.DrawRectangle(square, HandleBorderColor, 1f);
        }
    }

    /// <summary>The child's outline. When rotated, it is drawn as a polyline through
    /// the four rotated corners — the canvas wouldn't rotate a rectangle.</summary>
    private void DrawOutline(Graphics g, Rectangle bounds)
    {
        if (OutlineWidth <= 0 || OutlineColor.A == 0) return;

        if (ChildRotation == 0f)
        {
            g.DrawRectangle(bounds, OutlineColor, OutlineWidth);
            return;
        }

        Point center = bounds.Center;

        Span<Point> corners =
        [
            RotateAround(new Point(bounds.X, bounds.Y), center, ChildRotation),
            RotateAround(new Point(bounds.Right, bounds.Y), center, ChildRotation),
            RotateAround(new Point(bounds.Right, bounds.Bottom), center, ChildRotation),
            RotateAround(new Point(bounds.X, bounds.Bottom), center, ChildRotation),
            RotateAround(new Point(bounds.X, bounds.Y), center, ChildRotation),
        ];

        g.DrawPolyline(corners, OutlineColor, OutlineWidth);
    }

    private Rectangle HandleRect(GripKind grip, Rectangle bounds)
    {
        Point center = HandleCenter(grip, bounds);
        float radius = HandleSize / 2f;

        return new Rectangle(
            new Point(center.X - radius, center.Y - radius),
            new Size(HandleSize, HandleSize));
    }

    /// <summary>The center of a handle in GripBox coordinates,
    /// already accounting for the child's rotation.</summary>
    private Point HandleCenter(GripKind grip, Rectangle b)
    {
        Point raw = grip switch
        {
            GripKind.TopLeft => new Point(b.X, b.Y),
            GripKind.Top => new Point(b.Center.X, b.Y),
            GripKind.TopRight => new Point(b.Right, b.Y),
            GripKind.Right => new Point(b.Right, b.Center.Y),
            GripKind.BottomRight => new Point(b.Right, b.Bottom),
            GripKind.Bottom => new Point(b.Center.X, b.Bottom),
            GripKind.BottomLeft => new Point(b.X, b.Bottom),
            GripKind.Left => new Point(b.X, b.Center.Y),
            GripKind.Rotate => new Point(b.Center.X, b.Y - RotateHandleOffset),
            _ => b.Center,
        };

        return ChildRotation == 0f ? raw : RotateAround(raw, b.Center, ChildRotation);
    }

    private GripKind GripAt(Point local)
    {
        if (Child is null) return GripKind.None;

        Rectangle bounds = ChildBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return GripKind.None;

        // rotation is checked first: its handle sticks out and competes with nothing
        if (AllowRotate && Point.DistanceBetween(local, HandleCenter(GripKind.Rotate, bounds)) <= HitRadius)
            return GripKind.Rotate;

        if (!AllowResize) return GripKind.None;

        foreach (GripKind grip in ResizeGrips)
        {
            if (Point.DistanceBetween(local, HandleCenter(grip, bounds)) <= HitRadius)
                return grip;
        }

        return GripKind.None;
    }

    /// <summary>A click on a handle must not fall through into the child:
    /// the corner handles lie right on its border.</summary>
    protected internal override bool HitTestSelfFirst(Point localPoint) =>
        GripAt(localPoint) != GripKind.None;

    protected override void OnMouseDown(MouseButtonEventArgs args)
    {
        if (Child is null || args.Button != MouseButton.Left) return;

        Point local = ToLocal(args.Location);
        GripKind grip = GripAt(local);

        if (grip == GripKind.None) return;

        _active = grip;
        _dragStart = local;

        // from the starting values, not from the current ones at every step —
        // otherwise drift accumulates
        _startSize = Child.ActualSize;
        _startExplicitSize = Child.Size;
        _startMargin = Margin;
        _startRotation = Child.Rotation;
        _startAngle = AngleTo(local);

        // without capture the drag breaks off as soon as the cursor leaves the window
        CaptureMouse();

        args.Handled = true;
    }

    protected override void OnMouseMove(MouseMoveEventArgs args)
    {
        Point local = ToLocal(args.Location);

        if (_active == GripKind.None)
        {
            UpdateHover(local);
            return;
        }

        if (Child is null) return;

        if (_active == GripKind.Rotate)
            Rotate(local);
        else
            Resize(local);
    }

    protected override void OnMouseUp(MouseButtonEventArgs args)
    {
        if (_active == GripKind.None) return;

        _active = GripKind.None;
        ReleaseMouseCapture();
    }

    /// <summary>The interaction was cut off — the system took the capture away,
    /// the box left the tree. Previously _active stayed set here, and after that
    /// simply hovering with no button pressed kept resizing or rotating the child.
    /// Nothing was committed, so the child goes back to how it was before the press.</summary>
    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        if (_active == GripKind.None) return;

        GripKind interrupted = _active;
        _active = GripKind.None;

        if (Child is null) return;

        if (interrupted == GripKind.Rotate)
        {
            Child.Rotation = _startRotation;
            ChildRotated?.Invoke(this, EventArgs.Empty);
            return;
        }

        Child.Size = _startExplicitSize;

        if (!Margin.Equals(_startMargin))
            Margin = _startMargin;

        Invalidate();
        ChildResized?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseExit(MouseMoveEventArgs args)
    {
        if (_active != GripKind.None || _hovered == GripKind.None) return;

        _hovered = GripKind.None;
        Cursor = CursorKind.Default;
    }

    private void UpdateHover(Point local)
    {
        GripKind grip = GripAt(local);
        if (grip == _hovered) return;

        _hovered = grip;

        // CursorKind has no diagonal cursors, SizeAll is used for the corners
        Cursor = grip switch
        {
            GripKind.Left or GripKind.Right => CursorKind.SizeWestEast,
            GripKind.Top or GripKind.Bottom => CursorKind.SizeNorthSouth,
            GripKind.TopLeft or GripKind.TopRight or
            GripKind.BottomLeft or GripKind.BottomRight => CursorKind.SizeAll,
            GripKind.Rotate => CursorKind.Hand,
            _ => CursorKind.Default,
        };
    }

    private void Resize(Point local)
    {
        var delta = new Point(local.X - _dragStart.X, local.Y - _dragStart.Y);

        // the handles live in the rotated child's coordinate system,
        // so the cursor offset is rotated back
        if (ChildRotation != 0f)
            delta = RotateAround(delta, Point.Empty, -ChildRotation);

        float dw = _active switch
        {
            GripKind.Left or GripKind.TopLeft or GripKind.BottomLeft => -delta.X,
            GripKind.Right or GripKind.TopRight or GripKind.BottomRight => delta.X,
            _ => 0f,
        };

        float dh = _active switch
        {
            GripKind.Top or GripKind.TopLeft or GripKind.TopRight => -delta.Y,
            GripKind.Bottom or GripKind.BottomLeft or GripKind.BottomRight => delta.Y,
            _ => 0f,
        };

        float width = Math.Max(MinChildSize.Width, _startSize.Width + dw);
        float height = Math.Max(MinChildSize.Height, _startSize.Height + dh);

        Child!.Size = new Size(width, height);

        AnchorEdges(width, height);

        Invalidate();
        ChildResized?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Dragging the left or top side — the opposite edge is held in place
    /// by shifting ourselves with the margin by the same amount.</summary>
    private void AnchorEdges(float width, float height)
    {
        if (!AnchorOppositeEdge || ChildRotation != 0f) return;

        bool movesLeft = _active is GripKind.Left or GripKind.TopLeft or GripKind.BottomLeft;
        bool movesTop = _active is GripKind.Top or GripKind.TopLeft or GripKind.TopRight;

        if (!movesLeft && !movesTop) return;

        Margin = new Thickness(
            movesLeft ? _startMargin.Left - (width - _startSize.Width) : _startMargin.Left,
            movesTop ? _startMargin.Top - (height - _startSize.Height) : _startMargin.Top,
            _startMargin.Right,
            _startMargin.Bottom);
    }

    private void Rotate(Point local)
    {
        float angle = _startRotation + (AngleTo(local) - _startAngle);

        if (RotationSnap > 0f)
            angle = MathF.Round(angle / RotationSnap) * RotationSnap;

        // normalize, otherwise after a few revolutions the number
        // would run into thousands of degrees
        angle %= 360f;
        if (angle < 0f) angle += 360f;

        if (Math.Abs(Child!.Rotation - angle) < 0.01f) return;

        Child.Rotation = angle;

        InvalidateVisual();
        ChildRotated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The angle from the child's center to the point, in degrees.</summary>
    private float AngleTo(Point local)
    {
        Point center = ChildBounds.Center;

        return MathF.Atan2(local.Y - center.Y, local.X - center.X) * 180f / MathF.PI;
    }

    private Point ToLocal(Point absolute)
    {
        Point origin = GetAbsolutePosition();

        return new Point(absolute.X - origin.X, absolute.Y - origin.Y);
    }
}