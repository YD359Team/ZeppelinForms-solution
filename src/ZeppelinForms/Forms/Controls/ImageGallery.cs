using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>How a thumbnail fills its cell.</summary>
public enum GalleryStretch : byte
{
    /// <summary>Covers the cell, cropping what doesn't fit: an even grid.</summary>
    Fill,

    /// <summary>Fits whole inside the cell, leaving bands: nothing is cut off.</summary>
    Uniform,
}

/// <summary>An item of a gallery was opened: double-clicked, or Enter.</summary>
public sealed class GalleryItemEventArgs(int index, GalleryItem item) : EventArgs
{
    public int Index { get; } = index;

    public GalleryItem Item { get; } = item;
}

/// <summary>
/// A grid of thumbnails that scrolls: photos, attachments, a picture library.
/// Selection as in a list, the arrows move through the grid, and an item opened
/// shows in an <see cref="ImageViewer"/> over the whole window.
/// </summary>
/// <remarks>
/// <para>
/// The gallery draws its items rather than holding a control for each: thousands of
/// pictures cost only the visible ones. Thumbnails that are loaded rather than
/// given are asked for as they scroll into view, a few at a time
/// (<see cref="MaxConcurrentLoads"/>), and only while they are still in view.
/// </para>
/// <para>
/// The columns follow the width: as many cells of <see cref="ThumbnailWidth"/> as
/// fit, widened to fill the row, their height kept in the proportion of
/// <see cref="ThumbnailWidth"/> to <see cref="ThumbnailHeight"/>.
/// </para>
/// </remarks>
public partial class ImageGallery : DecoratedPanel, IInputElement
{
    // the lead is moved by the arrows; a range is counted from the anchor
    private readonly SortedSet<int> _selected = [];
    private int _selectedIndex = -1;
    private int _anchor = -1;
    private int _hovered = -1;

    // the grid as of the last layout
    private int _columns = 1;
    private float _cellWidth;
    private float _cellHeight;

    // loading: what is waiting, how many run, and the generation they belong to
    private readonly List<GalleryItem> _waiting = [];
    private int _running;
    private int _loadGeneration;
    private CancellationTokenSource _loads = new();

    private ImageViewer? _viewer;

    public ImageGallery()
    {
        OverflowY = Overflow.Auto;

        Items.CollectionChanged += OnItemsChanged;
    }

    public ObservableCollection<GalleryItem> Items { get; } = [];

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    // ===== layout settings =====

    /// <summary>The smallest width of a cell; cells widen to fill the row.</summary>
    [Styled(Category = "Thumbnails", AffectsLayout = true)]
    public partial float ThumbnailWidth { get; set; }
    private static float ThumbnailWidthDefault => 140f;

    /// <summary>The height of a cell of <see cref="ThumbnailWidth"/>; a wider cell
    /// keeps the proportion.</summary>
    [Styled(Category = "Thumbnails", AffectsLayout = true)]
    public partial float ThumbnailHeight { get; set; }
    private static float ThumbnailHeightDefault => 140f;

    /// <summary>The gap between cells, across and down.</summary>
    [Styled(Category = "Thumbnails", AffectsLayout = true)]
    public partial float ItemSpacing { get; set; }
    private static float ItemSpacingDefault => 8f;

    [Styled(Category = "Thumbnails")]
    public partial GalleryStretch Stretch { get; set; }

    [Styled(Category = "Thumbnails")]
    public partial float ThumbnailCornerRadius { get; set; }
    private static float ThumbnailCornerRadiusDefault => 4f;

    /// <summary>The titles under the thumbnails. Without them a title is the
    /// thumbnail's tooltip.</summary>
    [Styled(Category = "Thumbnails", AffectsLayout = true)]
    public partial bool ShowTitles { get; set; }

    /// <summary>How many thumbnails load at once.</summary>
    public int MaxConcurrentLoads { get; set; } = 4;

    // ===== colors =====

    /// <summary>A cell whose picture isn't there yet.</summary>
    [Styled(Category = "Thumbnails")]
    public partial Color PlaceholderColor { get; set; }
    private static Color PlaceholderColorDefault => new(255, 232, 232, 232);

    /// <summary>Over the thumbnail under the pointer.</summary>
    [Styled(Category = "Thumbnails")]
    public partial Color HoverColor { get; set; }
    private static Color HoverColorDefault => new(36, 0, 0, 0);

    /// <summary>The frame and the badge of a selected thumbnail.</summary>
    [Styled(Category = "Selection")]
    public partial Color SelectionColor { get; set; }
    private static Color SelectionColorDefault => new(255, 0, 120, 215);

    /// <summary>The check mark in the badge.</summary>
    [Styled(Category = "Selection")]
    public partial Color CheckColor { get; set; }
    private static Color CheckColorDefault => Colors.White;

    /// <summary>The ring around the thumbnail the keyboard is on.</summary>
    [Styled(Category = "Selection")]
    public partial Color FocusColor { get; set; }
    private static Color FocusColorDefault => new(255, 26, 26, 26);

    [Styled(Category = "Thumbnails")]
    public partial Color TitleColor { get; set; }
    private static Color TitleColorDefault => Colors.Black;

    /// <summary>The mark of a picture that failed to load.</summary>
    [Styled(Category = "Thumbnails")]
    public partial Color GlyphColor { get; set; }
    private static Color GlyphColorDefault => new(255, 140, 140, 140);

    /// <summary>Under the viewer an opened item is shown in.</summary>
    [Styled(Category = "Viewer")]
    public partial Color ViewerBackground { get; set; }
    private static Color ViewerBackgroundDefault => new(235, 0, 0, 0);

    // ===== selection =====

    [Styled(Category = "Selection")]
    public partial SelectionMode SelectionMode { get; set; }

    /// <summary>The lead item: the one the arrows move, and with multiple selection
    /// the one selected last.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SelectOnly(value < 0 || value >= Items.Count ? -1 : value);
    }

    public GalleryItem? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;

    /// <summary>All selected indices in ascending order.</summary>
    public IReadOnlyCollection<int> SelectedIndices => _selected;

    public IReadOnlyList<GalleryItem> SelectedItems =>
        [.. _selected.Where(i => i < Items.Count).Select(i => Items[i])];

    public event EventHandler? SelectionChanged;

    /// <summary>An item was opened: double-clicked, or Enter.</summary>
    public event EventHandler<GalleryItemEventArgs>? ItemActivated;

    /// <summary>Whether an opened item shows in a viewer over the window.</summary>
    public bool OpenViewerOnActivate { get; set; } = true;

    public bool IsSelected(int index) => _selected.Contains(index);

    public void ClearSelection()
    {
        if (_selected.Count == 0 && _selectedIndex < 0) return;

        _selected.Clear();
        _selectedIndex = -1;
        _anchor = -1;

        RaiseSelectionChanged();
    }

    public void SelectAll()
    {
        if (SelectionMode == SelectionMode.Single || Items.Count == 0) return;

        _selected.Clear();

        for (int i = 0; i < Items.Count; i++)
            _selected.Add(i);

        _selectedIndex = Items.Count - 1;
        _anchor = 0;

        RaiseSelectionChanged();
    }

    public void SetSelected(int index, bool selected)
    {
        if (index < 0 || index >= Items.Count) return;

        bool changed = selected ? _selected.Add(index) : _selected.Remove(index);

        if (!changed) return;

        if (selected) _selectedIndex = index;
        else if (_selectedIndex == index) _selectedIndex = _selected.Count > 0 ? _selected.Max : -1;

        RaiseSelectionChanged();
    }

    private void SelectOnly(int index)
    {
        bool same = _selectedIndex == index && _selected.Count <= 1 &&
                    (index < 0 || _selected.Contains(index));

        if (same) return;

        _selected.Clear();

        if (index >= 0) _selected.Add(index);

        _selectedIndex = index;
        _anchor = index;

        RaiseSelectionChanged();
    }

    private void SelectRange(int to)
    {
        if (_anchor < 0) { SelectOnly(to); return; }

        _selected.Clear();

        for (int i = Math.Min(_anchor, to); i <= Math.Max(_anchor, to); i++)
            _selected.Add(i);

        _selectedIndex = to;

        RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ===== items =====

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (GalleryItem item in e.OldItems)
                item.Changed -= OnItemChanged;

        if (e.NewItems is not null)
            foreach (GalleryItem item in e.NewItems)
                item.Changed += OnItemChanged;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                ShiftSelection(e.NewStartingIndex, e.NewItems!.Count);
                break;

            case NotifyCollectionChangedAction.Remove:
                DropSelection(e.OldStartingIndex, e.OldItems!.Count);
                break;

            case NotifyCollectionChangedAction.Replace:
                break;

            default:
                // a move or a reset: where each selected item went is not worth
                // reconstructing, and a stale selection is worse than none
                foreach (GalleryItem item in Items)
                {
                    item.Changed -= OnItemChanged;
                    item.Changed += OnItemChanged;
                }

                if (_selected.Count > 0 || _selectedIndex >= 0)
                {
                    _selected.Clear();
                    _selectedIndex = _anchor = -1;
                    RaiseSelectionChanged();
                }

                break;
        }

        _hovered = -1;

        // :empty follows the items, not children
        if (FindOwner() is not null)
            RestyleAfterChildrenChanged();

        Invalidate();
    }

    /// <summary>Items were inserted: the selection moves along with what it marked.</summary>
    private void ShiftSelection(int start, int count)
    {
        if (_selected.Count == 0 && _selectedIndex < start && _anchor < start) return;

        int[] moved = [.. _selected.Select(i => i >= start ? i + count : i)];
        _selected.Clear();
        _selected.UnionWith(moved);

        if (_selectedIndex >= start) _selectedIndex += count;
        if (_anchor >= start) _anchor += count;

        RaiseSelectionChanged();
    }

    /// <summary>Items were removed: their selection goes with them, the rest moves up.</summary>
    private void DropSelection(int start, int count)
    {
        int end = start + count;

        bool affected = _selected.Any(i => i >= start) || _selectedIndex >= start || _anchor >= start;
        if (!affected) return;

        int[] kept = [.. _selected.Where(i => i < start || i >= end).Select(i => i >= end ? i - count : i)];
        _selected.Clear();
        _selected.UnionWith(kept);

        _selectedIndex = _selectedIndex < start ? _selectedIndex
            : _selectedIndex >= end ? _selectedIndex - count
            : (_selected.Count > 0 ? _selected.Max : -1);

        _anchor = _anchor < start ? _anchor : _anchor >= end ? _anchor - count : _selectedIndex;

        RaiseSelectionChanged();
    }

    private void OnItemChanged(object? sender, EventArgs e)
    {
        // a title shown under the thumbnail changes nothing in the layout: the
        // caption band is there for every item alike
        InvalidateVisual();

        if (sender is GalleryItem item && Items.IndexOf(item) == _hovered)
            RefreshToolTip();
    }

    // ===== geometry =====

    private float Spacing => Math.Max(0f, ItemSpacing);

    private float CaptionHeight =>
        ShowTitles ? TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height + 8f : 0f;

    private float RowHeight => _cellHeight + CaptionHeight;

    private float RowPitch => RowHeight + Spacing;

    private int RowCount => (Items.Count + _columns - 1) / _columns;

    /// <summary>How many columns there are now.</summary>
    public int Columns => _columns;

    /// <summary>Fit the columns into the width, and size the cells.</summary>
    private void ComputeGrid(float width)
    {
        float target = Math.Max(1f, ThumbnailWidth);
        float ratio = Math.Max(1f, ThumbnailHeight) / target;
        float gap = Spacing;

        if (!float.IsFinite(width))
        {
            // nothing to fit into: one row of cells at their own size
            _columns = Math.Max(1, Items.Count);
            _cellWidth = target;
        }
        else
        {
            _columns = Math.Max(1, (int)((width + gap) / (target + gap)));
            _cellWidth = Math.Max(1f, (width - gap * (_columns - 1)) / _columns);
        }

        _cellHeight = _cellWidth * ratio;
    }

    private float ContentHeight
    {
        get
        {
            int rows = RowCount;

            return rows == 0 ? 0f : rows * RowHeight + (rows - 1) * Spacing;
        }
    }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        float width = float.IsFinite(availableSize.Width)
            ? Math.Max(0f, availableSize.Width - Padding.Horizontal)
            : float.PositiveInfinity;

        ComputeGrid(width);

        float contentWidth = float.IsFinite(width)
            ? availableSize.Width
            : _columns * _cellWidth + (_columns - 1) * Spacing + Padding.Horizontal;

        return new Size(contentWidth, ContentHeight + Padding.Vertical);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        ComputeGrid(Math.Max(0f, contentSize.Width - Padding.Horizontal));

        // the scroll position is settled now: what is in view is known
        RequestVisibleLoads();
    }

    /// <summary>The picture part of a cell, in the gallery's coordinates as it is
    /// scrolled now. The title, when shown, goes under it.</summary>
    public Rectangle ThumbnailBounds(int index)
    {
        if (index < 0 || index >= Items.Count) return default;

        int row = index / _columns;
        int column = index % _columns;

        // right to left, the first item is at the right
        if (IsRightToLeft) column = _columns - 1 - column;

        return new Rectangle(
            new Point(
                Padding.Left + column * (_cellWidth + Spacing) - ScrollX,
                Padding.Top + row * RowPitch - ScrollY),
            new Size(_cellWidth, _cellHeight));
    }

    /// <summary>The item at a point of the gallery, picture or title; −1 in a gap.</summary>
    public int IndexAt(Point local)
    {
        if (Items.Count == 0 || _cellWidth <= 0f) return -1;

        float x = local.X - Padding.Left + ScrollX;
        float y = local.Y - Padding.Top + ScrollY;

        if (x < 0f || y < 0f) return -1;

        int column = (int)(x / (_cellWidth + Spacing));
        int row = (int)(y / RowPitch);

        // in the gap after a cell, not on it
        if (x - column * (_cellWidth + Spacing) > _cellWidth) return -1;
        if (y - row * RowPitch > RowHeight) return -1;

        if (column >= _columns) return -1;

        if (IsRightToLeft) column = _columns - 1 - column;

        int index = row * _columns + column;

        return index < Items.Count ? index : -1;
    }

    /// <summary>The rows in view, with <paramref name="lookahead"/> more on each side.</summary>
    /// <remarks>
    /// From the size of the last arrange, not <see cref="PanelControl.Viewport"/>:
    /// the loads are queued from inside the arrange, where ActualSize is still the
    /// previous one — on the first layout, nothing, and only the first row was
    /// thought to be in view.
    /// </remarks>
    private (int First, int Last) VisibleRange(int lookahead)
    {
        if (Items.Count == 0 || RowPitch <= 0f) return (0, -1);

        float height = ArrangedViewport.Height;

        int firstRow = Math.Max(0, (int)((ScrollY - Padding.Top) / RowPitch) - lookahead);
        int lastRow = (int)((ScrollY + height - Padding.Top) / RowPitch) + lookahead;

        int first = firstRow * _columns;
        int last = Math.Min(Items.Count - 1, (lastRow + 1) * _columns - 1);

        return (first, last);
    }

    /// <summary>Scroll so that the item is in view whole.</summary>
    public void ScrollIntoView(int index)
    {
        if (index < 0 || index >= Items.Count) return;

        Rectangle view = Viewport;

        float top = Padding.Top + index / _columns * RowPitch;
        float bottom = top + RowHeight;

        float scroll = ScrollY;

        if (top - scroll < view.Y) scroll = top - view.Y;
        else if (bottom - scroll > view.Y + view.Height) scroll = bottom - view.Y - view.Height;

        if (scroll != ScrollY) ScrollTo(ScrollX, scroll);
    }

    // ===== loading =====

    /// <summary>Queue the thumbnails now in view, and only those: one that scrolled
    /// away before its turn came is not loaded after all.</summary>
    private void RequestVisibleLoads()
    {
        _waiting.Clear();

        if (FindOwner() is null) return;

        (int first, int last) = VisibleRange(lookahead: 1);

        for (int i = first; i <= last; i++)
            if (Items[i].NeedsThumbnail)
                _waiting.Add(Items[i]);

        PumpLoads();
    }

    private void PumpLoads()
    {
        Form? form = FindOwner();

        if (form is null) return;

        while (_running < Math.Max(1, MaxConcurrentLoads) && _waiting.Count > 0)
        {
            GalleryItem item = _waiting[0];
            _waiting.RemoveAt(0);

            if (!item.NeedsThumbnail) continue;

            int generation = _loadGeneration;
            _running++;

            item.Load(full: false, form, _loads.Token, () =>
            {
                // a load of a gallery that has since left the form counts no more
                if (generation != _loadGeneration) return;

                _running--;
                PumpLoads();
            });
        }
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        // the layout that follows queues what is in view
        Invalidate();
    }

    protected override void OnDetached()
    {
        _loadGeneration++;
        _running = 0;
        _waiting.Clear();

        _loads.Cancel();
        _loads.Dispose();
        _loads = new CancellationTokenSource();

        CloseViewer();

        base.OnDetached();
    }

    // ===== drawing =====

    protected override void DrawContent(Graphics g)
    {
        if (Items.Count == 0) return;

        (int first, int last) = VisibleRange(lookahead: 0);

        g.Save();
        g.ClipRect(Viewport);

        float radiusValue = Math.Max(0f, ThumbnailCornerRadius);
        var radius = new CornerRadius(radiusValue);

        for (int i = first; i <= last; i++)
            DrawItem(g, i, radius);

        g.Restore();
    }

    private void DrawItem(Graphics g, int index, CornerRadius radius)
    {
        GalleryItem item = Items[index];
        Rectangle cell = ThumbnailBounds(index);

        g.FillRoundRectangle(cell, radius, PlaceholderColor);

        if (item.GridImage is { } image)
        {
            g.Save();
            g.ClipRoundRect(cell, radius);

            if (Stretch == GalleryStretch.Fill)
                g.DrawImage(Cover(cell, image), image, ImageFlip.None, ImageLayout.Stretch);
            else
                g.DrawImage(cell, image, ImageFlip.None, ImageLayout.Zoom);

            g.Restore();
        }
        else if (item.ThumbnailState == GalleryImageState.Failed)
        {
            DrawBrokenImage(g,
                new Point(cell.X + cell.Width / 2f, cell.Y + cell.Height / 2f),
                Math.Min(32f, Math.Min(cell.Width, cell.Height) * 0.4f),
                GlyphColor);
        }

        if (index == _hovered && HoverColor.A > 0)
            g.FillRoundRectangle(cell, radius, HoverColor);

        if (_selected.Contains(index))
            DrawSelection(g, cell, radius);

        if (index == _selectedIndex && IsFocusVisible && FocusColor.A > 0)
        {
            float gap = 3f;
            g.DrawRoundRectangle(Grow(cell, gap), Grow(radius, gap), FocusColor, 2f);
        }

        if (ShowTitles && !string.IsNullOrEmpty(item.Title))
        {
            var caption = new Rectangle(
                new Point(cell.X, cell.Y + cell.Height),
                new Size(cell.Width, CaptionHeight));

            g.Save();
            g.ClipRect(caption);
            g.DrawText(ApplyTextTransform(item.Title), caption, TitleColor, EffectiveFont,
                HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
            g.Restore();
        }
    }

    /// <summary>A frame inside the cell, and a badge with a check in the corner
    /// where lines end — a selection that reads without color too.</summary>
    private void DrawSelection(Graphics g, Rectangle cell, CornerRadius radius)
    {
        const float frame = 3f;

        g.DrawRoundRectangle(Grow(cell, -frame / 2f), Grow(radius, -frame / 2f), SelectionColor, frame);

        float size = Math.Min(22f, Math.Min(cell.Width, cell.Height) * 0.3f);
        float inset = 6f;
        float x = IsRightToLeft ? cell.X + inset : cell.X + cell.Width - inset - size;

        var badge = new Rectangle(new Point(x, cell.Y + inset), new Size(size, size));

        g.FillEllipse(badge, SelectionColor);

        float cx = badge.X + size / 2f;
        float cy = badge.Y + size / 2f;
        float r = size * 0.25f;

        g.DrawPolyline(
            [new(cx - r, cy), new(cx - r * 0.25f, cy + r * 0.75f), new(cx + r, cy - r * 0.7f)],
            CheckColor, Math.Max(1.5f, size / 10f));
    }

    /// <summary>The rectangle a picture is drawn into to cover a cell: scaled to
    /// the larger of the two fits, centered, the excess clipped off.</summary>
    private static Rectangle Cover(Rectangle cell, Image image)
    {
        float scale = Math.Max(cell.Width / image.Width, cell.Height / image.Height);

        float width = image.Width * scale;
        float height = image.Height * scale;

        return new Rectangle(
            new Point(cell.X + (cell.Width - width) / 2f, cell.Y + (cell.Height - height) / 2f),
            new Size(width, height));
    }

    /// <summary>A picture that failed: a frame with a mountain, crossed out.</summary>
    internal static void DrawBrokenImage(Graphics g, Point center, float size, Color color)
    {
        float half = size / 2f;
        float stroke = Math.Max(1.5f, size / 16f);

        var frame = new Rectangle(new Point(center.X - half, center.Y - half * 0.8f), new Size(size, size * 0.8f));

        g.DrawRoundRectangle(frame, new CornerRadius(size * 0.08f), color, stroke);

        g.DrawPolyline(
            [
                new(frame.X + size * 0.12f, frame.Y + frame.Height * 0.8f),
                new(frame.X + size * 0.4f, frame.Y + frame.Height * 0.45f),
                new(frame.X + size * 0.6f, frame.Y + frame.Height * 0.65f),
                new(frame.X + size * 0.88f, frame.Y + frame.Height * 0.35f),
            ],
            color, stroke);

        g.DrawLine(
            new Point(frame.X - size * 0.08f, frame.Y + frame.Height + size * 0.08f),
            new Point(frame.X + size + size * 0.08f, frame.Y - size * 0.08f),
            color, stroke);
    }

    private static Rectangle Grow(Rectangle rect, float amount) => new(
        new Point(rect.X - amount, rect.Y - amount),
        new Size(Math.Max(0f, rect.Width + amount * 2f), Math.Max(0f, rect.Height + amount * 2f)));

    private static CornerRadius Grow(CornerRadius radius, float amount) => new(
        Math.Max(0f, radius.TopLeft + amount),
        Math.Max(0f, radius.TopRight + amount),
        Math.Max(0f, radius.BottomRight + amount),
        Math.Max(0f, radius.BottomLeft + amount));

    // ===== mouse =====

    private Point ToLocal(Point absolute)
    {
        Point origin = GetAbsolutePosition();

        return new Point(absolute.X - origin.X, absolute.Y - origin.Y);
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        base.OnMouseMove(e);

        int index = IndexAt(ToLocal(e.Location));

        if (index == _hovered) return;

        _hovered = index;

        RefreshToolTip();
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        base.OnMouseExit(e);

        if (_hovered < 0) return;

        _hovered = -1;
        InvalidateVisual();
    }

    /// <summary>Selection on press, as in a list. Not on the scrollbar: the panel
    /// drags it, and the item under it is not being chosen.</summary>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;

        Point local = ToLocal(e.Location);

        // the scrollbar is the panel's: a press there scrolls, it doesn't choose
        if (HitTestSelfFirst(local)) return;

        int index = IndexAt(local);
        if (index < 0) return;

        switch (SelectionMode)
        {
            case SelectionMode.Multiple:
                SetSelected(index, !IsSelected(index));
                _anchor = index;
                break;

            case SelectionMode.Extended when e.Modifiers.HasFlag(KeyModifiers.Shift):
                SelectRange(index);
                break;

            case SelectionMode.Extended when e.Modifiers.HasFlag(KeyModifiers.Control):
                SetSelected(index, !IsSelected(index));
                _anchor = index;
                break;

            default:
                SelectOnly(index);
                break;
        }
    }

    protected override void OnDoubleClick(MouseClickEventArgs e)
    {
        if (e.Button != MouseButton.Left) return;

        int index = IndexAt(ToLocal(e.Location));

        if (index < 0) return;

        Activate(index);
        e.Handled = true;
    }

    protected internal override string? GetToolTip(Point location)
    {
        // with the titles on screen a tooltip would only say them twice
        if (!ShowTitles && IndexAt(ToLocal(location)) is var index and >= 0)
            return Items[index].Title ?? Items[index].Description ?? ToolTip;

        return ToolTip;
    }

    // ===== keyboard =====

    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        InvalidateVisual();
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Items.Count == 0)
        {
            base.OnKeyDown(e);
            return;
        }

        int lead = _selectedIndex;
        int forward = IsRightToLeft ? -1 : 1;
        int pageRows = Math.Max(1, (int)(Viewport.Height / Math.Max(1f, RowPitch)));

        int target = e.Key switch
        {
            Key.Left => lead < 0 ? 0 : lead - forward,
            Key.Right => lead < 0 ? 0 : lead + forward,
            Key.Up => lead < 0 ? 0 : lead - _columns,
            Key.Down => lead < 0 ? 0 : lead + _columns,
            Key.PageUp => lead < 0 ? 0 : lead - _columns * pageRows,
            Key.PageDown => lead < 0 ? 0 : lead + _columns * pageRows,
            Key.Home => 0,
            Key.End => Items.Count - 1,
            _ => int.MinValue,
        };

        if (target != int.MinValue)
        {
            // Left and Right stop at the ends of the list; Up and Down stay in the
            // column, but the last row may be short — the last item stands in
            target = e.Key is Key.Left or Key.Right
                ? Math.Clamp(target, 0, Items.Count - 1)
                : target < 0 ? (e.Key is Key.Up or Key.PageUp ? lead % _columns : 0)
                : Math.Min(target, Items.Count - 1);

            if (SelectionMode == SelectionMode.Extended && e.Modifiers.HasFlag(KeyModifiers.Shift))
                SelectRange(target);
            else
                SelectOnly(target);

            ScrollIntoView(target);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when lead >= 0:
                Activate(lead);
                e.Handled = true;
                return;

            case Key.Space when lead >= 0 && SelectionMode != SelectionMode.Single:
                SetSelected(lead, !IsSelected(lead));
                if (!IsSelected(lead)) _selectedIndex = lead;
                e.Handled = true;
                return;

            case Key.A when e.Modifiers.HasFlag(KeyModifiers.Control):
                SelectAll();
                e.Handled = true;
                return;
        }

        base.OnKeyDown(e);
    }

    // ===== opening =====

    /// <summary>Open an item: <see cref="ItemActivated"/>, then the viewer when
    /// <see cref="OpenViewerOnActivate"/>.</summary>
    public void Activate(int index)
    {
        if (index < 0 || index >= Items.Count) return;

        ItemActivated?.Invoke(this, new GalleryItemEventArgs(index, Items[index]));

        if (OpenViewerOnActivate)
            ShowViewer(index);
    }

    public bool IsViewerOpen => _viewer is not null;

    /// <summary>The viewer while it is open.</summary>
    public ImageViewer? Viewer => _viewer;

    public event EventHandler? ViewerOpened;

    public event EventHandler? ViewerClosed;

    /// <summary>Show the items in a viewer over the whole window, starting with this one.</summary>
    public void ShowViewer(int index)
    {
        if (FindOwner() is not { } form || index < 0 || index >= Items.Count) return;

        if (_viewer is { } open)
        {
            open.Index = index;
            return;
        }

        var viewer = new ImageViewer
        {
            Items = Items,
            Index = index,
            ShowCloseButton = true,
            Background = ViewerBackground,
            Position = Point.Empty,
            Size = form.ClientSize,
            AccessibleName = AccessibleName,
        };

        viewer.CloseRequested += OnViewerCloseRequested;
        form.ClientSizeChanged += OnFormResized;

        _viewer = viewer;

        form.AddOverlay(viewer);
        form.FocusElement(viewer);

        ViewerOpened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Close the viewer: the gallery selects the item it was on, and takes
    /// the focus back.</summary>
    public void CloseViewer()
    {
        if (_viewer is not { } viewer) return;

        _viewer = null;

        viewer.CloseRequested -= OnViewerCloseRequested;

        Form? form = viewer.FindOwner();

        if (form is not null)
        {
            form.ClientSizeChanged -= OnFormResized;
            form.RemoveOverlay(viewer);
        }

        if (viewer.Index >= 0 && viewer.Index < Items.Count)
        {
            SelectOnly(viewer.Index);
            ScrollIntoView(viewer.Index);
        }

        if (FindOwner() is { } own)
            own.FocusElement(this);

        ViewerClosed?.Invoke(this, EventArgs.Empty);
    }

    private void OnViewerCloseRequested(object? sender, EventArgs e) => CloseViewer();

    private void OnFormResized(object? sender, EventArgs e)
    {
        if (_viewer is { } viewer && sender is Form form)
            viewer.Size = form.ClientSize;
    }
}