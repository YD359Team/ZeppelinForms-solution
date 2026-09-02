using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Две области с перетаскиваемым разделителем между ними.
/// </summary>
public class SplitContainer : PanelControl
{
    private UIElement? _first;
    private UIElement? _second;

    private bool _dragging;
    private float _dragOffset;
    private bool _splitterHovered;

    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    public float SplitterThickness { get; set; } = 6f;

    /// <summary>Положение разделителя от начала. Отрицательное — вычислить по SplitterRatio.</summary>
    public float SplitterPosition { get; set; } = -1f;

    /// <summary>Доля первой области, если позиция не задана явно.</summary>
    public float SplitterRatio { get; set; } = 0.5f;

    public float FirstMinSize { get; set; } = 40f;
    public float SecondMinSize { get; set; } = 40f;

    /// <summary>Панель, которая сохраняет размер при изменении контейнера.</summary>
    public SplitterFixedPanel FixedPanel { get; set; } = SplitterFixedPanel.None;

    public Color SplitterColor { get; set; } = new Color(255, 224, 224, 224);
    public Color SplitterHoverColor { get; set; } = new Color(255, 190, 190, 190);

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

        // порядок в Children определяет слот: 0 — первая область, 1 — вторая
        int index = slot == 0 ? 0 : Children.Count;
        Children.Insert(Math.Min(index, Children.Count), value);
    }

    private bool IsHorizontal => Orientation == Orientation.Horizontal;

    private float TotalExtent => IsHorizontal ? ContentBounds.Width : ContentBounds.Height;

    private float ResolvedPosition
    {
        get
        {
            float total = TotalExtent;
            float position = SplitterPosition >= 0 ? SplitterPosition : total * SplitterRatio;

            float max = Math.Max(FirstMinSize, total - SecondMinSize - SplitterThickness);

            return Math.Clamp(position, FirstMinSize, max);
        }
    }

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

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRectangle(LocalBounds, Background);

        g.FillRectangle(SplitterRect, _splitterHovered || _dragging ? SplitterHoverColor : SplitterColor);
    }

    // ===== ввод =====

    protected internal override bool HitTestSelfFirst(Point localPoint)
    {
        // разделитель принадлежит контейнеру, а не областям под ним
        Rectangle rect = SplitterRect;

        return localPoint.X >= rect.X && localPoint.X <= rect.X + rect.Width
            && localPoint.Y >= rect.Y && localPoint.Y <= rect.Y + rect.Height;
    }

    protected override void OnMouseExit(MouseMoveEventArgs args)
    {
        Point abs = GetAbsolutePosition();

        if (_dragging)
        {
            float position = IsHorizontal
                ? args.Location.X - abs.X - ContentBounds.X - _dragOffset
                : args.Location.Y - abs.Y - ContentBounds.Y - _dragOffset;

            SplitterPosition = position;
            SplitterMoved?.Invoke(this, EventArgs.Empty);
            Invalidate();
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

    protected override void OnMouseDown(MouseMoveEventArgs args)
    {
        Point abs = GetAbsolutePosition();
        var local = new Point(args.Location.X - abs.X, args.Location.Y - abs.Y);

        if (!HitTestSelfFirst(local)) return;

        _dragging = true;

        // запоминаем, за какую точку разделителя схватились,
        // иначе он прыгнет под курсор при первом же движении
        _dragOffset = IsHorizontal
            ? local.X - SplitterRect.X
            : local.Y - SplitterRect.Y;
    }

    protected override void OnMouseUp(MouseMoveEventArgs args)
    {
        _dragging = false;
    }

    protected override void OnMouseLeave()
    {
        _splitterHovered = false;
        Cursor = CursorKind.Default;
    }

    // ===== раскладка =====

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        float position = SplitterPosition >= 0 ? SplitterPosition : GetExtent(inner) * SplitterRatio;
        float rest = Math.Max(0, GetExtent(inner) - position - SplitterThickness);

        _first?.Measure(WithExtent(inner, position));
        _second?.Measure(WithExtent(inner, rest));

        return inner;
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        var area = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, contentSize.Width - Padding.Horizontal),
                Math.Max(0, contentSize.Height - Padding.Vertical)));

        float total = GetExtent(area.Size);
        float position = SplitterPosition >= 0 ? SplitterPosition : total * SplitterRatio;

        float max = Math.Max(FirstMinSize, total - SecondMinSize - SplitterThickness);
        position = Math.Clamp(position, FirstMinSize, max);

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

    protected override void OnSizeChanged()
    {
        // при изменении контейнера фиксированная панель сохраняет размер,
        // а вторая забирает разницу
        if (FixedPanel == SplitterFixedPanel.Second && SplitterPosition >= 0)
        {
            float total = TotalExtent;
            SplitterPosition = Math.Max(FirstMinSize, total - SecondFixedExtent - SplitterThickness);
        }
        else if (FixedPanel == SplitterFixedPanel.None && SplitterPosition >= 0)
        {
            // пропорциональный режим: пересчитываем долю
            SplitterRatio = TotalExtent > 0 ? SplitterPosition / TotalExtent : 0.5f;
            SplitterPosition = -1f;
        }
    }

    private float SecondFixedExtent { get; set; }

    private float GetExtent(Size size) => IsHorizontal ? size.Width : size.Height;

    private Size WithExtent(Size size, float extent) =>
        IsHorizontal ? new Size(extent, size.Height) : new Size(size.Width, extent);
}

public enum SplitterFixedPanel { None, First, Second }
