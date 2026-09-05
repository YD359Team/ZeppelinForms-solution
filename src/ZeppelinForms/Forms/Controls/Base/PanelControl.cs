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

    // _contentSize приходит из MeasureContentOverride вместе с отступами,
    // поэтому вычитаем полный размер, а не ContentBounds — иначе отступ
    // засчитается дважды и прокрутить можно будет за конец содержимого
    private float MaxScrollX => Math.Max(0, _contentSize.Width - ActualSize.Width + ReservedWidth);
    private float MaxScrollY => Math.Max(0, _contentSize.Height - ActualSize.Height + ReservedHeight);

    public ScrollBarMode ScrollBarMode { get; set; } = ScrollBarMode.Overlay;

    // видимость считается один раз за раскладку и дальше только читается:
    // в режиме Inline полосы отнимают место друг у друга, и пересчёт
    // на каждом обращении давал бы разные ответы в разных местах кадра
    private bool _verticalBar;
    private bool _horizontalBar;

    protected bool ShowVerticalBar => _verticalBar;
    protected bool ShowHorizontalBar => _horizontalBar;

    private float ReservedWidth => ScrollBarMode == ScrollBarMode.Inline && _verticalBar ? ScrollBarThickness : 0f;
    private float ReservedHeight => ScrollBarMode == ScrollBarMode.Inline && _horizontalBar ? ScrollBarThickness : 0f;

    /// <summary>Видимая область содержимого: ContentBounds за вычетом места
    /// под полосы. В режиме Overlay совпадает с ContentBounds.</summary>
    protected Rectangle Viewport
    {
        get
        {
            Rectangle c = ContentBounds;

            return new Rectangle(c.Position, new Size(
                Math.Max(0, c.Width - ReservedWidth),
                Math.Max(0, c.Height - ReservedHeight)));
        }
    }

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

    /// <summary>Нужны ли полосы при такой видимой области. В режиме Inline
    /// одна полоса может вызвать появление второй, поэтому после первой
    /// проверяем повторно — одного повтора достаточно, дальше размер
    /// уже не меняется.</summary>
    private (bool Vertical, bool Horizontal) ResolveBars(Size available)
    {
        bool NeedsVertical(float height) =>
            OverflowY == Overflow.Scroll ||
            (OverflowY == Overflow.Auto && _contentSize.Height - height > 0.5f);

        bool NeedsHorizontal(float width) =>
            OverflowX == Overflow.Scroll ||
            (OverflowX == Overflow.Auto && _contentSize.Width - width > 0.5f);

        bool vertical = NeedsVertical(available.Height);
        bool horizontal = NeedsHorizontal(available.Width);

        if (ScrollBarMode != ScrollBarMode.Inline)
            return (vertical, horizontal);

        if (vertical) horizontal = NeedsHorizontal(available.Width - ScrollBarThickness);
        if (horizontal) vertical = NeedsVertical(available.Height - ScrollBarThickness);

        return (vertical, horizontal);
    }

    // ===== прокрутка поверх обычной раскладки =====

    protected sealed override Size MeasureOverride(Size availableSize)
    {
        // по прокручиваемой оси даём содержимому расти сколько нужно
        var probe = new Size(
            ScrollsHorizontally ? float.PositiveInfinity : availableSize.Width,
            ScrollsVertically ? float.PositiveInfinity : availableSize.Height);

        _contentSize = MeasureContentOverride(probe);

        if (ScrollBarMode == ScrollBarMode.Inline)
        {
            var (vertical, horizontal) = ResolveBars(availableSize);

            if (vertical || horizontal)
            {
                _contentSize = MeasureContentOverride(new Size(
                    ScrollsHorizontally ? probe.Width : probe.Width - (vertical ? ScrollBarThickness : 0),
                    ScrollsVertically ? probe.Height : probe.Height - (horizontal ? ScrollBarThickness : 0)));
            }
        }

        // сама панель за пределы выделенного не выходит — излишек уезжает в прокрутку
        return new Size(
            ScrollsHorizontally ? Math.Min(_contentSize.Width, availableSize.Width) : _contentSize.Width,
            ScrollsVertically ? Math.Min(_contentSize.Height, availableSize.Height) : _contentSize.Height);
    }

    protected sealed override Size ArrangeOverride(Size finalSize)
    {
        // ContentBounds здесь ещё смотрит на прошлый ActualSize: он
        // присваивается только после возврата отсюда. Поэтому и видимость
        // полос, и предел прокрутки считаем от finalSize
        (_verticalBar, _horizontalBar) = ResolveBars(finalSize);

        float reservedW = ScrollBarMode == ScrollBarMode.Inline && _verticalBar ? ScrollBarThickness : 0f;
        float reservedH = ScrollBarMode == ScrollBarMode.Inline && _horizontalBar ? ScrollBarThickness : 0f;

        var viewport = new Size(
            Math.Max(0, finalSize.Width - reservedW),
            Math.Max(0, finalSize.Height - reservedH));

        // содержимое раскладывается в полный размер, даже если он больше панели
        var contentArea = new Size(
            ScrollsHorizontally ? Math.Max(viewport.Width, _contentSize.Width) : viewport.Width,
            ScrollsVertically ? Math.Max(viewport.Height, _contentSize.Height) : viewport.Height);

        ArrangeContentOverride(contentArea);

        if (IsRightToLeft)
        {
            // отражаем детей относительно вертикальной оси панели
            foreach (UIElement child in Children)
                child.Position = new Point(
                    contentArea.Width - child.Position.X - child.ActualSize.Width,
                    child.Position.Y);
        }

        float maxX = Math.Max(0, _contentSize.Width - viewport.Width);
        float maxY = Math.Max(0, _contentSize.Height - viewport.Height);

        ScrollX = Math.Clamp(ScrollX, 0, maxX);
        ScrollY = Math.Clamp(ScrollY, 0, maxY);

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
            float ratio = _contentSize.Height <= 0 ? 1 : Viewport.Height / _contentSize.Height;
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
            float ratio = _contentSize.Width <= 0 ? 1 : Viewport.Width / _contentSize.Width;
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

    protected override void OnMouseDown(MouseButtonEventArgs args)
    {
        Point abs = GetAbsolutePosition();
        var local = new Point(args.Location.X - abs.X, args.Location.Y - abs.Y);

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
                ScrollTo(ScrollX, ScrollY + (offsetInBar < pos ? -Viewport.Height : Viewport.Height));
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
                ScrollTo(ScrollX + (offsetInBar < pos ? -Viewport.Width : Viewport.Width), ScrollY);
            }
        }
    }

    protected override void OnMouseMove(MouseMoveEventArgs args)
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
                float t = (args.Location.Y - abs.Y - bar.Y - _dragOffset) / free;
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
                float t = (args.Location.X - abs.X - bar.X - _dragOffset) / free;
                ScrollTo(MaxScrollX * Math.Clamp(t, 0, 1), ScrollY);
            }
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs location)
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

public enum ScrollBarMode
{
    /// <summary>Полоса лежит поверх содержимого: места не занимает,
    /// но перекрывает то, что под ней.</summary>
    Overlay,

    /// <summary>Место под полосу вычитается из области содержимого:
    /// содержимое уже, зато ничего не перекрыто.</summary>
    Inline,
}
