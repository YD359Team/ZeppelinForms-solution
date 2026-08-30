using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Control with any count of children
/// </summary>
public abstract class PanelControl : UIElement
{
    protected const float ScrollBarThickness = 10f;
    private const float MinThumbLength = 24f;

    private Size _contentSize;
    private bool _draggingVertical;
    private bool _draggingHorizontal;
    private float _dragOffset;

    public ObservableCollection<UIElement> Children { get; } = [];

    public Overflow OverflowX { get; set; } = Overflow.Visible;
    public Overflow OverflowY { get; set; } = Overflow.Visible;

    public float ScrollX { get; private set; }
    public float ScrollY { get; private set; }

    public float WheelStep { get; set; } = 48f;

    public Color ScrollTrackColor { get; set; } = new Color(40, 0, 0, 0);
    public Color ScrollThumbColor { get; set; } = new Color(120, 0, 0, 0);

    protected bool ScrollsHorizontally => OverflowX is Overflow.Scroll or Overflow.Auto;
    protected bool ScrollsVertically => OverflowY is Overflow.Scroll or Overflow.Auto;

    private float MaxScrollX => Math.Max(0, _contentSize.Width - ContentBounds.Width);
    private float MaxScrollY => Math.Max(0, _contentSize.Height - ContentBounds.Height);

    private bool ShowVerticalBar => OverflowY == Overflow.Scroll || (OverflowY == Overflow.Auto && MaxScrollY > 0.5f);
    private bool ShowHorizontalBar => OverflowX == Overflow.Scroll || (OverflowX == Overflow.Auto && MaxScrollX > 0.5f);

    public PanelControl()
    {
        Children.CollectionChanged += Children_CollectionChanged;
    }

    private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Form? owner = FindOwner();

        if (e.OldItems is not null)
            foreach (UIElement item in e.OldItems)
            {
                owner?.DetachTree(item);
                item.Parent = null;
            }

        if (e.NewItems is not null)
            foreach (UIElement item in e.NewItems)
            {
                item.Parent = this;
                owner?.AttachTree(item);
            }

        Invalidate();
    }

    // ===== прокрутка поверх обычной раскладки =====

    protected sealed override Size MeasureOverride(Size availableSize)
    {
        // по прокручиваемой оси даём содержимому расти сколько нужно
        var probe = new Size(
            ScrollsHorizontally ? float.PositiveInfinity : availableSize.Width,
            ScrollsVertically ? float.PositiveInfinity : availableSize.Height);

        _contentSize = MeasureContentOverride(probe);

        // а сама панель за пределы выделенного не выходит — излишек уезжает в прокрутку
        return new Size(
            ScrollsHorizontally ? Math.Min(_contentSize.Width, availableSize.Width) : _contentSize.Width,
            ScrollsVertically ? Math.Min(_contentSize.Height, availableSize.Height) : _contentSize.Height);
    }

    protected sealed override Size ArrangeOverride(Size finalSize)
    {
        // содержимое раскладывается в полный размер, даже если он больше панели
        var contentArea = new Size(
            ScrollsHorizontally ? Math.Max(finalSize.Width, _contentSize.Width) : finalSize.Width,
            ScrollsVertically ? Math.Max(finalSize.Height, _contentSize.Height) : finalSize.Height);

        ArrangeContentOverride(contentArea);

        if (IsRightToLeft)
        {
            // отражаем детей относительно вертикальной оси панели
            foreach (UIElement child in Children)
                child.Position = new Point(
                    contentArea.Width - child.Position.X - child.ActualSize.Width,
                    child.Position.Y);
        }

        ScrollX = Math.Clamp(ScrollX, 0, MaxScrollX);
        ScrollY = Math.Clamp(ScrollY, 0, MaxScrollY);

        if (ScrollX != 0 || ScrollY != 0)
        {
            // сдвигаем уже размещённых детей — так конкретным панелям
            // не нужно ничего знать про прокрутку
            foreach (UIElement child in Children)
                child.Position = new Point(child.Position.X - ScrollX, child.Position.Y - ScrollY);
        }

        return finalSize;
    }

    /// <summary>Измерение содержимого, как в обычной панели.</summary>
    protected abstract Size MeasureContentOverride(Size availableSize);

    /// <summary>Размещение содержимого. Размер может превышать размер панели при прокрутке.</summary>
    protected abstract void ArrangeContentOverride(Size contentSize);

    public void ScrollTo(float x, float y)
    {
        ScrollX = Math.Clamp(x, 0, MaxScrollX);
        ScrollY = Math.Clamp(y, 0, MaxScrollY);
        Invalidate();
    }

    // ===== полосы =====

    private Rectangle VerticalBarRect
    {
        get
        {
            var c = ContentBounds;
            return new Rectangle(
                new Point(c.X + c.Width - ScrollBarThickness, c.Y),
                new Size(ScrollBarThickness, c.Height - (ShowHorizontalBar ? ScrollBarThickness : 0)));
        }
    }

    private Rectangle HorizontalBarRect
    {
        get
        {
            var c = ContentBounds;
            return new Rectangle(
                new Point(c.X, c.Y + c.Height - ScrollBarThickness),
                new Size(c.Width - (ShowVerticalBar ? ScrollBarThickness : 0), ScrollBarThickness));
        }
    }

    private (float Position, float Length) VerticalThumb
    {
        get
        {
            Rectangle bar = VerticalBarRect;
            float ratio = _contentSize.Height <= 0 ? 1 : ContentBounds.Height / _contentSize.Height;
            float length = Math.Max(MinThumbLength, bar.Height * Math.Min(1, ratio));
            float position = MaxScrollY <= 0 ? 0 : (bar.Height - length) * (ScrollY / MaxScrollY);
            return (position, length);
        }
    }

    private (float Position, float Length) HorizontalThumb
    {
        get
        {
            Rectangle bar = HorizontalBarRect;
            float ratio = _contentSize.Width <= 0 ? 1 : ContentBounds.Width / _contentSize.Width;
            float length = Math.Max(MinThumbLength, bar.Width * Math.Min(1, ratio));
            float position = MaxScrollX <= 0 ? 0 : (bar.Width - length) * (ScrollX / MaxScrollX);
            return (position, length);
        }
    }

    protected internal override void DrawOverlay(Graphics g)
    {
        if (ShowVerticalBar)
        {
            Rectangle bar = VerticalBarRect;
            var (pos, len) = VerticalThumb;

            g.FillRoundRectangle(bar, new CornerRadius(ScrollBarThickness / 2f), ScrollTrackColor);
            g.FillRoundRectangle(
                new Rectangle(new Point(bar.X + 2, bar.Y + pos), new Size(ScrollBarThickness - 4, len)),
                new CornerRadius((ScrollBarThickness - 4) / 2f), ScrollThumbColor);
        }

        if (ShowHorizontalBar)
        {
            Rectangle bar = HorizontalBarRect;
            var (pos, len) = HorizontalThumb;

            g.FillRoundRectangle(bar, new CornerRadius(ScrollBarThickness / 2f), ScrollTrackColor);
            g.FillRoundRectangle(
                new Rectangle(new Point(bar.X + pos, bar.Y + 2), new Size(len, ScrollBarThickness - 4)),
                new CornerRadius((ScrollBarThickness - 4) / 2f), ScrollThumbColor);
        }
    }

    protected internal override bool HitTestSelfFirst(Point localPoint)
    {
        // клики по полосе принадлежат панели, а не тому, что под ней
        if (ShowVerticalBar && Contains(VerticalBarRect, localPoint)) return true;
        if (ShowHorizontalBar && Contains(HorizontalBarRect, localPoint)) return true;

        return false;
    }

    private static bool Contains(Rectangle r, Point p) =>
        p.X >= r.X && p.X <= r.X + r.Width && p.Y >= r.Y && p.Y <= r.Y + r.Height;

    protected override void OnMouseDown(Point location)
    {
        Point abs = GetAbsolutePosition();
        var local = new Point(location.X - abs.X, location.Y - abs.Y);

        if (ShowVerticalBar && Contains(VerticalBarRect, local))
        {
            var (pos, len) = VerticalThumb;
            float offsetInBar = local.Y - VerticalBarRect.Y;

            if (offsetInBar >= pos && offsetInBar <= pos + len)
            {
                _draggingVertical = true;
                _dragOffset = offsetInBar - pos;
            }
            else
            {
                ScrollTo(ScrollX, ScrollY + (offsetInBar < pos ? -ContentBounds.Height : ContentBounds.Height));
            }

            return;
        }

        if (ShowHorizontalBar && Contains(HorizontalBarRect, local))
        {
            var (pos, len) = HorizontalThumb;
            float offsetInBar = local.X - HorizontalBarRect.X;

            if (offsetInBar >= pos && offsetInBar <= pos + len)
            {
                _draggingHorizontal = true;
                _dragOffset = offsetInBar - pos;
            }
            else
            {
                ScrollTo(ScrollX + (offsetInBar < pos ? -ContentBounds.Width : ContentBounds.Width), ScrollY);
            }
        }
    }

    protected override void OnMouseMove(Point location)
    {
        if (!_draggingVertical && !_draggingHorizontal) return;

        Point abs = GetAbsolutePosition();

        if (_draggingVertical)
        {
            Rectangle bar = VerticalBarRect;
            var (_, len) = VerticalThumb;
            float free = bar.Height - len;

            if (free > 0)
            {
                float t = (location.Y - abs.Y - bar.Y - _dragOffset) / free;
                ScrollTo(ScrollX, MaxScrollY * Math.Clamp(t, 0, 1));
            }
        }
        else
        {
            Rectangle bar = HorizontalBarRect;
            var (_, len) = HorizontalThumb;
            float free = bar.Width - len;

            if (free > 0)
            {
                float t = (location.X - abs.X - bar.X - _dragOffset) / free;
                ScrollTo(MaxScrollX * Math.Clamp(t, 0, 1), ScrollY);
            }
        }
    }

    protected override void OnMouseUp(Point location)
    {
        _draggingVertical = _draggingHorizontal = false;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!ScrollsVertically || MaxScrollY <= 0) return;

        float before = ScrollY;
        ScrollTo(ScrollX, ScrollY - e.Delta / 120f * WheelStep);

        if (Math.Abs(before - ScrollY) > 0.01f)
            e.Handled = true;
    }
}
