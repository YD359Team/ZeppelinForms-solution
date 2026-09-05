using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Обёртка с ручками вокруг содержимого: перетаскиванием меняет размер
/// и угол поворота ребёнка, как в визуальном редакторе.
/// Размер пишется в <see cref="UIElement.Size"/> ребёнка, угол — в
/// <see cref="UIElement.Rotation"/>; и то, и другое рендер и хит-тест
/// уже понимают, своих трансформаций GripBox не заводит.
/// </summary>
public class GripBox : DecoratedWrapControl
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

    public float HandleSize { get; set; } = 8f;

    /// <summary>Насколько ручка поворота вынесена над верхним краем.</summary>
    public float RotateHandleOffset { get; set; } = 24f;

    public Color HandleColor { get; set; } = Colors.White;
    public Color HandleBorderColor { get; set; } = new Color(255, 0, 120, 215);
    public Color OutlineColor { get; set; } = new Color(255, 0, 120, 215);
    public float OutlineWidth { get; set; } = 1f;

    public bool AllowResize { get; set; } = true;
    public bool AllowRotate { get; set; } = true;

    public Size MinChildSize { get; set; } = new(16f, 16f);

    /// <summary>Шаг привязки угла в градусах. 0 — поворот без привязки.</summary>
    public float RotationSnap { get; set; }

    /// <summary>
    /// Тянуть за левую или верхнюю ручку, оставляя противоположный край на месте.
    /// Работает через <see cref="UIElement.Margin"/> самого GripBox, поэтому
    /// осмысленно только в раскладке, где отступ реально смещает элемент,
    /// и только при нулевом повороте.
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

    /// <summary>Отступ под ручки, чтобы они не наезжали на содержимое.
    /// Вызывается заново, если поменяли <see cref="HandleSize"/>.</summary>
    public void ReserveGutter()
    {
        float gutter = HandleSize;

        Padding = new Thickness(
            gutter,
            gutter + (AllowRotate ? RotateHandleOffset : 0f),
            gutter,
            gutter);
    }

    /// <summary>Прямоугольник ребёнка в координатах GripBox.</summary>
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

    /// <summary>Контур ребёнка. При повороте рисуем ломаной по четырём
    /// повёрнутым углам — прямоугольник холст бы не повернул.</summary>
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

    /// <summary>Центр ручки в координатах GripBox, уже с учётом поворота ребёнка.</summary>
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

        // поворот проверяем первым: его ручка вынесена наружу и ни с чем не спорит
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

    /// <summary>Клик по ручке не должен проваливаться в ребёнка:
    /// угловые ручки лежат прямо на его границе.</summary>
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

        // от стартовых значений, а не от текущих на каждом шаге — иначе копится дрейф
        _startSize = Child.ActualSize;
        _startMargin = Margin;
        _startRotation = Child.Rotation;
        _startAngle = AngleTo(local);

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

    protected override void OnMouseUp(MouseButtonEventArgs args) => _active = GripKind.None;

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

        // диагональных курсоров в CursorKind нет, для углов берём SizeAll
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

        // ручки живут в системе координат повёрнутого ребёнка,
        // поэтому смещение курсора разворачиваем обратно
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

    /// <summary>Тянем за левую или верхнюю сторону — противоположный край
    /// держим на месте, сдвигая себя отступом на ту же величину.</summary>
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

        // нормализуем, иначе за несколько оборотов число уедет в тысячи градусов
        angle %= 360f;
        if (angle < 0f) angle += 360f;

        if (Math.Abs(Child!.Rotation - angle) < 0.01f) return;

        Child.Rotation = angle;

        InvalidateVisual();
        ChildRotated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Угол от центра ребёнка до точки, в градусах.</summary>
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

    private static Point RotateAround(Point point, Point center, float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);

        float dx = point.X - center.X;
        float dy = point.Y - center.Y;

        return new Point(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
    }
}