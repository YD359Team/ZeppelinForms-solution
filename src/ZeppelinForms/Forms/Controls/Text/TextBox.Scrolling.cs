using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>A multi-line field scrolls: with the wheel, its scroll bars and the caret.</summary>
/// <remarks>
/// <para>
/// Until 0.17 the field scrolled only to follow the caret: a long text could be
/// read only by moving the caret through it, the wheel did nothing and there were
/// no bars. And the caret was followed on every frame, so nothing else could have
/// scrolled the field anyway — the next frame would have brought it back. Now the
/// caret is followed when it moves, the text changes or the field is resized; in
/// between the field stays where the wheel or a bar put it.
/// </para>
/// <para>
/// The bars are the same as a panel's and the data grid's — one
/// <see cref="ScrollBarInteraction"/> for all of them: the thumb drags, the track
/// pages and keeps paging while held. They take their room from the text, so no
/// character hides under one. A single-line field has no bars and doesn't take the
/// wheel: it only follows the caret, as it always did.
/// </para>
/// </remarks>
public partial class TextBox
{
    private const float ScrollBarThickness = 10f;
    private const float MinThumbLength = 24f;

    /// <summary>Lines one notch of the wheel scrolls, as most systems have it.</summary>
    private const int WheelLines = 3;

    /// <summary>When the vertical bar of a multi-line field shows; <see cref="ScrollBarVisibility.Auto"/>
    /// by default — when the text is longer than the field.</summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = ScrollBarVisibility.Auto;

    /// <summary>When the horizontal bar of a multi-line field shows; <see cref="ScrollBarVisibility.Auto"/>
    /// by default — when a line is wider than the field. Never with <see cref="WordWrap"/>:
    /// wrapped lines fit the width.</summary>
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = ScrollBarVisibility.Auto;

    [Styled(Category = "Scrolling")]
    public partial Color ScrollTrackColor { get; set; }
    private static Color ScrollTrackColorDefault => new(40, 0, 0, 0);

    [Styled(Category = "Scrolling")]
    public partial Color ScrollThumbColor { get; set; }
    private static Color ScrollThumbColorDefault => new(120, 0, 0, 0);

    /// <summary>How far the text is scrolled down, in logical units.</summary>
    public float VerticalOffset => _verticalOffset;

    /// <summary>How far the text is scrolled sideways, in logical units.</summary>
    public float HorizontalOffset => _scrollOffset;

    /// <summary>Scroll the text to the given offsets, kept within the text. The caret
    /// stays where it is, and the field stays scrolled until the caret moves.</summary>
    public void ScrollTo(float horizontal, float vertical)
    {
        _scrollOffset = Math.Clamp(horizontal, 0, MaxHorizontalOffset);
        _verticalOffset = Math.Clamp(vertical, 0, MaxVerticalOffset);

        InvalidateVisual();
    }

    /// <summary>Bring the caret into view, as typing would.</summary>
    public void ScrollToCaret()
    {
        _followed = default;
        InvalidateVisual();
    }

    // ===== following the caret =====

    /// <summary>What the caret was followed for last: its index, the rows and the size
    /// of the text area. The caret is followed again only when one of them changes.</summary>
    private (int Caret, object? Rows, Size Area) _followed;

    /// <summary>Whether the caret is to be brought into view this frame.</summary>
    private bool TakeCaretFollow(int caret, IReadOnlyList<TextRow> rows, Rectangle area)
    {
        var now = (caret, (object?)rows, area.Size);

        if (_followed == now) return false;

        _followed = now;
        return true;
    }

    // ===== extents =====

    private float MaxVerticalOffset
    {
        get
        {
            if (!IsMultiline) return 0;

            Rectangle area = TextBounds;
            return Math.Max(0, Rows(area.Width).Count * LineHeight - area.Height);
        }
    }

    private float MaxHorizontalOffset
    {
        get
        {
            if (IsMultiline && WordWrap) return 0;

            Rectangle area = TextBounds;
            return Math.Max(0, ContentWidth(Rows(area.Width)) - area.Width);
        }
    }

    /// <summary>The width of the widest row: what the horizontal scroll reaches.</summary>
    private float ContentWidth(IReadOnlyList<TextRow> rows) =>
        IsMultiline ? WidestRow(rows) : RowLayout(rows, 0).Width;

    // ===== the bars =====

    private bool _verticalBar;
    private bool _horizontalBar;

    /// <summary>What the bars were decided for: the text, the size of the text column,
    /// the font and the settings. Deciding lays the rows out, so it is done once per
    /// change rather than on every frame and every hit test.</summary>
    private (string? Text, Size Column, Font? Font, bool Wrap, bool Multiline,
        ScrollBarVisibility Vertical, ScrollBarVisibility Horizontal) _barsFor;

    /// <summary>The text area with no bars taken out of it: the content between the icons.</summary>
    private Rectangle TextColumn
    {
        get
        {
            Rectangle content = this.ContentBounds;

            return new Rectangle(
                new Point(content.X + LeadingWidth, content.Y),
                new Size(Math.Max(0, content.Width - LeadingWidth - TrailingWidth), content.Height));
        }
    }

    /// <summary>Decide which bars show. A bar takes room, and the room changes what is
    /// needed — a vertical bar narrows the rows, a narrower wrap makes more of them —
    /// so it settles in a few steps, starting from the bars there are now: a keystroke
    /// that changes nothing about them lays the rows out at one width only.</summary>
    private void ResolveBars()
    {
        Rectangle column = TextColumn;
        string text = WithComposition(_document.Text);

        if (ReferenceEquals(_barsFor.Text, text) && _barsFor.Column == column.Size &&
            Equals(_barsFor.Font, EffectiveFont) && _barsFor.Wrap == WordWrap && _barsFor.Multiline == IsMultiline &&
            _barsFor.Vertical == VerticalScrollBarVisibility && _barsFor.Horizontal == HorizontalScrollBarVisibility)
            return;

        _barsFor = (text, column.Size, EffectiveFont, WordWrap, IsMultiline,
            VerticalScrollBarVisibility, HorizontalScrollBarVisibility);

        if (!IsMultiline)
        {
            _verticalBar = _horizontalBar = false;
            return;
        }

        bool vertical = _verticalBar;
        bool horizontal = _horizontalBar;

        for (int step = 0; step < 3; step++)
        {
            float width = Math.Max(0, column.Width - (vertical ? ScrollBarThickness : 0));
            float height = Math.Max(0, column.Height - (horizontal ? ScrollBarThickness : 0));

            IReadOnlyList<TextRow> rows = Rows(width);

            bool needsVertical = VerticalScrollBarVisibility switch
            {
                ScrollBarVisibility.Visible => true,
                ScrollBarVisibility.Hidden => false,
                _ => rows.Count * LineHeight > height,
            };

            bool needsHorizontal = !WordWrap && HorizontalScrollBarVisibility switch
            {
                ScrollBarVisibility.Visible => true,
                ScrollBarVisibility.Hidden => false,
                _ => WidestRow(rows) > width,
            };

            if (needsVertical == vertical && needsHorizontal == horizontal) break;

            vertical = needsVertical;
            horizontal = needsHorizontal;
        }

        _verticalBar = vertical;
        _horizontalBar = horizontal;
    }

    private Rectangle VerticalBarRect
    {
        get
        {
            Rectangle column = TextColumn;

            return new Rectangle(
                new Point(column.X + column.Width - ScrollBarThickness, column.Y),
                new Size(ScrollBarThickness, Math.Max(0, column.Height - (_horizontalBar ? ScrollBarThickness : 0))));
        }
    }

    private Rectangle HorizontalBarRect
    {
        get
        {
            Rectangle column = TextColumn;

            return new Rectangle(
                new Point(column.X, column.Y + column.Height - ScrollBarThickness),
                new Size(Math.Max(0, column.Width - (_verticalBar ? ScrollBarThickness : 0)), ScrollBarThickness));
        }
    }

    private (float Position, float Length) VerticalThumb
    {
        get
        {
            Rectangle bar = VerticalBarRect;
            Rectangle area = TextBounds;

            float content = Rows(area.Width).Count * LineHeight;
            return Thumb(bar.Height, area.Height, content, _verticalOffset, MaxVerticalOffset);
        }
    }

    private (float Position, float Length) HorizontalThumb
    {
        get
        {
            Rectangle bar = HorizontalBarRect;
            Rectangle area = TextBounds;

            float content = ContentWidth(Rows(area.Width));
            return Thumb(bar.Width, area.Width, content, _scrollOffset, MaxHorizontalOffset);
        }
    }

    private static (float Position, float Length) Thumb(float track, float view, float content, float offset, float max)
    {
        float ratio = content <= 0 ? 1 : view / content;
        float length = Math.Min(track, Math.Max(MinThumbLength, track * Math.Min(1, ratio)));
        float position = max <= 0 ? 0 : (track - length) * (offset / max);

        return (position, length);
    }

    private ScrollBarInteraction? _verticalBarInput;
    private ScrollBarInteraction? _horizontalBarInput;

    private ScrollBarInteraction VerticalBarInput => _verticalBarInput ??= new(
        this,
        Orientation.Vertical,
        () => VerticalBarRect,
        () => VerticalThumb,
        () => _verticalOffset,
        () => MaxVerticalOffset,
        () => TextBounds.Height,
        y => ScrollTo(_scrollOffset, y));

    private ScrollBarInteraction HorizontalBarInput => _horizontalBarInput ??= new(
        this,
        Orientation.Horizontal,
        () => HorizontalBarRect,
        () => HorizontalThumb,
        () => _scrollOffset,
        () => MaxHorizontalOffset,
        () => TextBounds.Width,
        x => ScrollTo(x, _verticalOffset));

    private void DrawScrollBars(Graphics g)
    {
        if (_verticalBar)
        {
            Rectangle bar = VerticalBarRect;
            var (position, length) = VerticalThumb;

            g.FillRoundRectangle(bar, new CornerRadius(ScrollBarThickness / 2f), ScrollTrackColor);
            g.FillRoundRectangle(
                new Rectangle(new Point(bar.X + 2, bar.Y + position), new Size(ScrollBarThickness - 4, length)),
                new CornerRadius((ScrollBarThickness - 4) / 2f), ScrollThumbColor);
        }

        if (_horizontalBar)
        {
            Rectangle bar = HorizontalBarRect;
            var (position, length) = HorizontalThumb;

            g.FillRoundRectangle(bar, new CornerRadius(ScrollBarThickness / 2f), ScrollTrackColor);
            g.FillRoundRectangle(
                new Rectangle(new Point(bar.X + position, bar.Y + 2), new Size(length, ScrollBarThickness - 4)),
                new CornerRadius((ScrollBarThickness - 4) / 2f), ScrollThumbColor);
        }
    }

    // ===== input =====

    private Point ToLocal(Point location)
    {
        Point abs = GetAbsolutePosition();
        return new Point(location.X - abs.X, location.Y - abs.Y);
    }

    private bool IsOverScrollBar(Point location)
    {
        ResolveBars();
        Point local = ToLocal(location);

        return (_verticalBar && Contains(VerticalBarRect, local)) ||
               (_horizontalBar && Contains(HorizontalBarRect, local));
    }

    private static bool Contains(Rectangle r, Point p) =>
        p.X >= r.X && p.X <= r.X + r.Width && p.Y >= r.Y && p.Y <= r.Y + r.Height;

    /// <summary>A left press on a bar: the thumb is grabbed or the track pages. False —
    /// not on a bar, and the press is the text's.</summary>
    private bool PressScrollBar(Point location)
    {
        ResolveBars();
        Point local = ToLocal(location);

        if (_verticalBar && VerticalBarInput.Press(local)) return true;
        if (_horizontalBar && HorizontalBarInput.Press(local)) return true;

        return false;
    }

    /// <summary>The pointer moved while a bar holds the press. False — no bar does.</summary>
    private bool MoveScrollBar(Point location)
    {
        if (_verticalBarInput is not { IsActive: true } && _horizontalBarInput is not { IsActive: true }) return false;

        Point local = ToLocal(location);

        if (_verticalBarInput?.Move(local) == true) return true;

        return _horizontalBarInput?.Move(local) == true;
    }

    /// <summary>The press ended. True — it was a bar's.</summary>
    private bool ReleaseScrollBar()
    {
        bool active = _verticalBarInput is { IsActive: true } || _horizontalBarInput is { IsActive: true };

        _verticalBarInput?.Release();
        _horizontalBarInput?.Release();

        return active;
    }

    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        base.OnPointerCanceled(e);

        // no release follows a cancel: a held track would page on and on
        ReleaseScrollBar();
        _isDragging = false;
    }

    /// <summary>The wheel scrolls a multi-line field by lines; Shift or a horizontal
    /// wheel scrolls it sideways. At either end the wheel goes on to the panel around
    /// the field, as it does from a list.</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e.Handled || !IsMultiline) return;

        float before = _verticalOffset;
        float beforeX = _scrollOffset;
        float step = WheelLines * LineHeight;

        if (e.HorizontalDelta != 0)
            ScrollTo(_scrollOffset - e.HorizontalDelta / 120f * step, _verticalOffset);
        else if (Input.Keyboard.Keyboard.IsShiftDown)
            ScrollTo(_scrollOffset - e.Delta / 120f * step, _verticalOffset);
        else
            ScrollTo(_scrollOffset, _verticalOffset - e.Delta / 120f * step);

        if (Math.Abs(before - _verticalOffset) > 0.01f || Math.Abs(beforeX - _scrollOffset) > 0.01f)
            e.Handled = true;
    }

    // ===== the row caches =====

    /// <summary>The breaks of each line at a width: a keystroke changes one line, and
    /// only that line is broken again — breaking measures every word, and a long
    /// text used to be broken whole on every key. Two widths are kept: deciding the
    /// bars tries the width with the vertical bar and without it.</summary>
    private readonly List<WrapCache> _wrapCaches = [];

    private sealed class WrapCache(float width, Font font, FlowDirection direction)
    {
        public float Width { get; } = width;
        public Font Font { get; } = font;
        public FlowDirection Direction { get; } = direction;

        /// <summary>The breaks of the lines the text had at the last layout; the lines
        /// that went are dropped at the next one, so the cache is never larger than the text.</summary>
        public Dictionary<string, List<(int Start, int Length)>> Lines { get; set; } = new(StringComparer.Ordinal);
    }

    private WrapCache WrapCacheFor(float width, Font font, FlowDirection direction)
    {
        foreach (WrapCache cache in _wrapCaches)
            if (cache.Width == width && Equals(cache.Font, font) && cache.Direction == direction)
                return cache;

        if (_wrapCaches.Count == 2) _wrapCaches.RemoveAt(0);

        var created = new WrapCache(width, font, direction);
        _wrapCaches.Add(created);

        return created;
    }

    /// <summary>The widths of the lines without wrapping, kept as the breaks are.</summary>
    private Dictionary<string, float> _lineWidths = new(StringComparer.Ordinal);
    private Font? _lineWidthsFont;

    private IReadOnlyList<TextRow>? _widestFor;
    private float _widest;

    /// <summary>The width of the widest of the rows, measured once per line text.</summary>
    private float WidestRow(IReadOnlyList<TextRow> rows)
    {
        if (ReferenceEquals(rows, _widestFor)) return _widest;

        Font font = EffectiveFont;

        if (!Equals(_lineWidthsFont, font))
        {
            _lineWidths.Clear();
            _lineWidthsFont = font;
        }

        var next = new Dictionary<string, float>(StringComparer.Ordinal);
        float widest = 0;

        foreach (TextRow row in rows)
        {
            if (!next.TryGetValue(row.Text, out float width))
            {
                if (!_lineWidths.TryGetValue(row.Text, out width))
                    width = TextMeasurer.MeasureLine(row.Text, font).Width;

                next[row.Text] = width;
            }

            widest = Math.Max(widest, width);
        }

        _lineWidths = next;
        _widestFor = rows;
        _widest = widest;

        return widest;
    }
}
