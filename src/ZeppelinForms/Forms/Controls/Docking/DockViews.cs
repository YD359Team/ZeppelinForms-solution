using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls.Docking;

// The elements a DockHost is made of. They are the host's: made from its layout, colored
// by its styled properties, and they ask it to do whatever changes the layout.

/// <summary>The small buttons of the panels' headers.</summary>
internal enum DockGlyph : byte
{
    Close,

    /// <summary>A pin upright: pinned — a click lets it slide away (auto hide).</summary>
    Pin,

    /// <summary>A pin on its side: sliding — a click docks it back.</summary>
    Unpin,
}

/// <summary>A row of header buttons from the right edge, with their hover and press.</summary>
internal sealed class DockButtons
{
    private readonly List<(DockGlyph Glyph, Action Act)> _buttons = [];

    private int _hovered = -1;
    private int _pressed = -1;

    public int Count => _buttons.Count;

    public void Set(params ReadOnlySpan<(DockGlyph, Action)> buttons)
    {
        _buttons.Clear();
        _buttons.AddRange(buttons);

        _hovered = -1;
        _pressed = -1;
    }

    /// <summary>The square of button <paramref name="index"/>, counted from the right edge
    /// of <paramref name="header"/>.</summary>
    public static Rectangle RectOf(Rectangle header, int index)
    {
        float side = header.Height;

        return new Rectangle(header.Right - side * (index + 1), header.Y, side, side);
    }

    public int IndexAt(Rectangle header, Point local)
    {
        for (int i = 0; i < _buttons.Count; i++)
            if (RectOf(header, i).Contains(local)) return i;

        return -1;
    }

    public float Width(float height) => _buttons.Count * height;

    public DockGlyph GlyphAt(int index) => _buttons[index].Glyph;

    /// <summary>True — the hover changed, and the header is to be drawn again.</summary>
    public bool Hover(int index)
    {
        if (_hovered == index) return false;

        _hovered = index;
        return true;
    }

    public bool Press(int index)
    {
        _pressed = index;
        return index >= 0;
    }

    /// <summary>The release: the button acts when it lands on the button pressed.</summary>
    public bool Release(int index)
    {
        int pressed = _pressed;
        _pressed = -1;

        if (pressed < 0 || pressed != index || index >= _buttons.Count) return pressed >= 0;

        _buttons[index].Act();
        return true;
    }

    public void Cancel() => _pressed = -1;

    public void Draw(Graphics g, Rectangle header, Color color, Color hover)
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            Rectangle rect = RectOf(header, i);
            Rectangle disk = rect.Inflate(-4);

            if (i == _hovered) g.FillRoundRectangle(disk, new CornerRadius(3), hover);

            DockGlyphs.Draw(g, _buttons[i].Glyph, disk, color);
        }
    }

    public static string ToolTipOf(DockGlyph glyph) => glyph switch
    {
        DockGlyph.Close => Localization.Get(ZfText.DockClose),
        DockGlyph.Pin => Localization.Get(ZfText.DockAutoHide),
        _ => Localization.Get(ZfText.DockBack),
    };
}

internal static class DockGlyphs
{
    public static void Draw(Graphics g, DockGlyph glyph, Rectangle rect, Color color)
    {
        Point c = rect.Center;
        float r = Math.Min(rect.Width, rect.Height) * 0.28f;

        switch (glyph)
        {
            case DockGlyph.Close:
                g.DrawLine(new Point(c.X - r, c.Y - r), new Point(c.X + r, c.Y + r), color, 1.3f);
                g.DrawLine(new Point(c.X + r, c.Y - r), new Point(c.X - r, c.Y + r), color, 1.3f);
                break;

            case DockGlyph.Pin:
                // the head, the collar, the needle: upright
                g.DrawRectangle(new Rectangle(c.X - r * 0.5f, c.Y - r, r, r * 1.1f), color, 1.2f);
                g.DrawLine(new Point(c.X - r, c.Y + r * 0.1f), new Point(c.X + r, c.Y + r * 0.1f), color, 1.2f);
                g.DrawLine(new Point(c.X, c.Y + r * 0.1f), new Point(c.X, c.Y + r * 1.1f), color, 1.2f);
                break;

            case DockGlyph.Unpin:
                // the same pin lying down, the needle to the left
                g.DrawRectangle(new Rectangle(c.X - r * 0.1f, c.Y - r * 0.5f, r * 1.1f, r), color, 1.2f);
                g.DrawLine(new Point(c.X - r * 0.1f, c.Y - r), new Point(c.X - r * 0.1f, c.Y + r), color, 1.2f);
                g.DrawLine(new Point(c.X - r * 1.1f, c.Y), new Point(c.X - r * 0.1f, c.Y), color, 1.2f);
                break;
        }
    }
}

/// <summary>A group: its items as tabs, the selected one's content under them.</summary>
internal sealed class DockGroupView : DecoratedPanel
{
    private readonly DockHost _host;
    private readonly TabStrip _strip;
    private readonly DockButtons _buttons = new();

    /// <summary>The strip is being made to match the group: its events are its own echo.</summary>
    private bool _syncing;

    /// <summary>The tab the right button was pressed on: the menu is for it.</summary>
    private int _menuTab = -1;

    public DockGroupView(DockHost host, DockGroup group)
    {
        _host = host;
        Group = group;

        _strip = new TabStrip { CanDragOut = true, MinTabWidth = 64f };
        _strip.SelectionChanged += OnTabSelected;
        _strip.TabCloseRequested += OnTabClose;
        _strip.TabMoved += OnTabMoved;
        _strip.TabDraggedOut += OnTabDraggedOut;
    }

    public DockGroup Group { get; }

    internal TabStrip Strip => _strip;

    /// <summary>A floating window of one item says its title in its own title bar.</summary>
    private bool ShowsStrip => Group.Float is null || Group.Items.Count > 1;

    private float HeaderHeight => ShowsStrip ? _strip.DesiredSize.Height : 0f;

    private Rectangle Header => new(0, 0, ActualSize.Width, HeaderHeight);

    /// <summary>Where the selected item's content is, in the view's coordinates.</summary>
    internal Rectangle ContentArea
    {
        get
        {
            float top = HeaderHeight;
            return new Rectangle(0, top, ActualSize.Width, Math.Max(0, ActualSize.Height - top));
        }
    }

    /// <summary>Make the strip and the content match the group.</summary>
    public void Sync()
    {
        _syncing = true;

        try
        {
            bool same = _strip.Items.Count == Group.Items.Count;

            for (int i = 0; same && i < Group.Items.Count; i++)
                same = ReferenceEquals(_strip.Items[i].Tag, Group.Items[i]);

            if (!same)
            {
                _strip.Items.Clear();

                foreach (DockItem item in Group.Items)
                    _strip.Items.Add(new TabStripItem { Tag = item });
            }

            foreach (TabStripItem tab in _strip.Items)
            {
                var item = (DockItem)tab.Tag!;

                tab.Header = item.Title;
                tab.Icon = item.Icon;
                tab.ToolTip = item.ToolTip;
                tab.IsClosable = item.CanClose;
            }

            _strip.SelectedItem = _strip.Items.FirstOrDefault(tab => ReferenceEquals(tab.Tag, Group.Selected));
        }
        finally
        {
            _syncing = false;
        }

        // a docked group can be pinned away as a whole
        if (Group.Float is null && Group.Selected is { CanAutoHide: true })
            _buttons.Set((DockGlyph.Pin, () => _host.AutoHideGroup(Group)));
        else
            _buttons.Set();

        List<UIElement> wanted = ShowsStrip ? [_strip] : [];

        if (Group.Selected?.Content is { } content)
            wanted.Add(content);

        DockHost.SetChildren(this, wanted);

        Invalidate();
    }

    private void OnTabSelected(object? sender, EventArgs e)
    {
        if (_syncing || _strip.SelectedItem?.Tag is not DockItem item) return;

        _host.Select(Group, item);
    }

    private void OnTabClose(object? sender, TabCloseRequestedEventArgs e)
    {
        // the host closes it — or doesn't: the strip only shows the group
        e.Cancel = true;

        if (e.Item.Tag is DockItem item) _host.Close(item);
    }

    private void OnTabMoved(object? sender, TabMovedEventArgs e)
    {
        if (_syncing || e.Item.Tag is not DockItem item) return;

        Group.Items.Remove(item);
        Group.Items.Insert(Math.Clamp(e.NewIndex, 0, Group.Items.Count), item);

        _host.RaiseLayoutChanged();
    }

    private void OnTabDraggedOut(object? sender, TabDraggedOutEventArgs e)
    {
        if (e.Item.Tag is DockItem item) _host.BeginItemDrag(item, Group, e.Location);
    }

    // ===== layout =====

    protected override Size MeasureContentOverride(Size availableSize)
    {
        float buttons = _buttons.Width(_strip.DesiredSize.Height > 0 ? _strip.DesiredSize.Height : 30f);
        float header = 0f;

        if (ShowsStrip)
        {
            _strip.Measure(new Size(Math.Max(0, availableSize.Width - buttons), float.PositiveInfinity));
            header = _strip.DesiredSize.Height;
        }

        Size content = Size.Empty;

        if (Group.Selected?.Content is { } element && ReferenceEquals(element.Parent, this))
        {
            element.Measure(new Size(availableSize.Width, Math.Max(0, availableSize.Height - header)));
            content = element.DesiredSize;
        }

        return ResolveSize(
            new Size(
                float.IsFinite(availableSize.Width) ? availableSize.Width : content.Width,
                float.IsFinite(availableSize.Height) ? availableSize.Height : content.Height + header),
            availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        float header = ShowsStrip ? _strip.DesiredSize.Height : 0f;

        if (ShowsStrip)
            _strip.Arrange(new Rectangle(0, 0, Math.Max(0, contentSize.Width - _buttons.Width(header)), header));

        // the content fills the panel whatever its alignment says, as a window's does
        if (Group.Selected?.Content is { } element && ReferenceEquals(element.Parent, this))
            element.Arrange(new Rectangle(1, header, Math.Max(0, contentSize.Width - 2), Math.Max(0, contentSize.Height - header - 1)), fill: true);
    }

    // ===== drawing =====

    protected override void DrawContent(Graphics g)
    {
        if (!ShowsStrip) return;

        Rectangle header = Header;
        float buttons = _buttons.Width(header.Height);

        if (buttons > 0)
        {
            g.FillRectangle(new Rectangle(header.Right - buttons, 0, buttons, header.Height), _strip.StripColor);
            _buttons.Draw(g, header, TextColor, _host.ButtonHoverColor);
        }
    }

    protected override void DrawDecoration(Graphics g)
    {
        // the active group is outlined in the accent: that is where the keys go
        Color border = _host.ActiveGroup == Group ? _host.AccentColor : _host.PanelBorderColor;

        if (border.A == 0) return;

        g.DrawRectangle(g.AlignToPixels(ContentArea), border, 1f);
    }

    // ===== input =====

    protected internal override bool HitTestSelfFirst(Point localPoint) =>
        ShowsStrip && _buttons.IndexAt(Header, localPoint) >= 0;

    private Point ToLocal(Point location)
    {
        Point at = GetAbsolutePosition();
        return new Point(location.X - at.X, location.Y - at.Y);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        _menuTab = e.Button == MouseButton.Right && ShowsStrip ? _strip.TabIndexAt(e.Location) : -1;

        _host.ActivateGroup(Group);
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (_buttons.Hover(_buttons.IndexAt(Header, ToLocal(e.Location))))
        {
            InvalidateVisual();
            RefreshToolTip();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (_buttons.Hover(-1)) InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.Button == MouseButton.Left && _buttons.Press(_buttons.IndexAt(Header, ToLocal(e.Location))))
        {
            e.Handled = true;
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (e.Button == MouseButton.Left && _buttons.Release(_buttons.IndexAt(Header, ToLocal(e.Location))))
            return;

        base.OnMouseUp(e);
    }

    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        _buttons.Cancel();
        base.OnPointerCanceled(e);
    }

    /// <summary>A double click on a tab floats it — or docks it back from a window.</summary>
    protected override void OnDoubleClick(MouseClickEventArgs e)
    {
        if (!ShowsStrip || _strip.TabIndexAt(e.Location) is not (>= 0 and var index) || index >= Group.Items.Count) return;

        DockItem item = Group.Items[index];

        if (Group.Float is not null) _host.Redock(item);
        else if (item.CanFloat) _host.Float(item);

        e.Handled = true;
    }

    protected internal override string? GetToolTip(Point location)
    {
        int index = _buttons.IndexAt(Header, ToLocal(location));

        return index >= 0 ? DockButtons.ToolTipOf(_buttons.GlyphAt(index)) : base.GetToolTip(location);
    }

    /// <summary>The menu of the tab the right button was pressed on; none over the content.</summary>
    protected override List<MenuItem>? GetDefaultContextMenu()
    {
        if (_menuTab < 0 || _menuTab >= Group.Items.Count) return null;

        return _host.MenuFor(Group.Items[_menuTab]);
    }
}

/// <summary>A split: the views of its children side by side or one under the other, with
/// a splitter between each two.</summary>
internal sealed class DockSplitView : DecoratedPanel
{
    private const float MinPane = 40f;

    private readonly DockHost _host;

    /// <summary>The extents the children were arranged with: the splitter drags from them.</summary>
    private float[] _extents = [];

    private int _hoveredGap = -1;
    private int _dragGap = -1;
    private float _dragStart;
    private float _startA;
    private float _startB;
    private float _weightA;
    private float _weightB;

    public DockSplitView(DockHost host, DockSplit split)
    {
        _host = host;
        Split = split;
    }

    public DockSplit Split { get; }

    private bool IsHorizontal => Split.Orientation == Orientation.Horizontal;

    private float Gap => _host.SplitterThickness;

    private int Count => Math.Min(Children.Count, Split.Children.Count);

    private float[] Extents(float total)
    {
        int count = Count;
        float[] extents = new float[count];

        if (count == 0) return extents;

        float available = Math.Max(0, total - Gap * (count - 1));
        float weights = 0f;

        for (int i = 0; i < count; i++) weights += Math.Max(0.0001f, Split.Children[i].Weight);

        for (int i = 0; i < count; i++)
            extents[i] = available * Math.Max(0.0001f, Split.Children[i].Weight) / weights;

        return extents;
    }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        float total = IsHorizontal ? availableSize.Width : availableSize.Height;

        if (!float.IsFinite(total))
        {
            foreach (UIElement child in Children) child.Measure(availableSize);

            return ResolveSize(new Size(300, 200), availableSize);
        }

        float[] extents = Extents(total);

        for (int i = 0; i < extents.Length; i++)
            Children[i].Measure(IsHorizontal
                ? new Size(extents[i], availableSize.Height)
                : new Size(availableSize.Width, extents[i]));

        return ResolveSize(availableSize, availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        _extents = Extents(IsHorizontal ? contentSize.Width : contentSize.Height);

        float offset = 0f;

        for (int i = 0; i < _extents.Length; i++)
        {
            Children[i].Arrange(IsHorizontal
                ? new Rectangle(offset, 0, _extents[i], contentSize.Height)
                : new Rectangle(0, offset, contentSize.Width, _extents[i]), fill: true);

            offset += _extents[i] + Gap;
        }
    }

    private Rectangle GapRect(int index)
    {
        float offset = 0f;

        for (int i = 0; i <= index && i < _extents.Length; i++) offset += _extents[i];

        offset += Gap * index;

        return IsHorizontal
            ? new Rectangle(offset, 0, Gap, ActualSize.Height)
            : new Rectangle(0, offset, ActualSize.Width, Gap);
    }

    private int GapAt(Point local)
    {
        for (int i = 0; i < _extents.Length - 1; i++)
            if (GapRect(i).Inflate(1f).Contains(local)) return i;

        return -1;
    }

    protected override void DrawContent(Graphics g)
    {
        for (int i = 0; i < _extents.Length - 1; i++)
            g.FillRectangle(GapRect(i), i == _hoveredGap || i == _dragGap ? _host.SplitterHoverColor : _host.SplitterColor);
    }

    protected internal override bool HitTestSelfFirst(Point localPoint) => GapAt(localPoint) >= 0;

    private Point ToLocal(Point location)
    {
        Point at = GetAbsolutePosition();
        return new Point(location.X - at.X, location.Y - at.Y);
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        Point local = ToLocal(e.Location);

        if (_dragGap >= 0)
        {
            DragTo(IsHorizontal ? local.X : local.Y);
            return;
        }

        int gap = GapAt(local);

        if (gap != _hoveredGap)
        {
            _hoveredGap = gap;
            Cursor = gap < 0 ? CursorKind.Default : IsHorizontal ? CursorKind.SizeWestEast : CursorKind.SizeNorthSouth;
            InvalidateVisual();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (_hoveredGap < 0 || _dragGap >= 0) return;

        _hoveredGap = -1;
        Cursor = CursorKind.Default;
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Point local = ToLocal(e.Location);
        int gap = e.Button == MouseButton.Left ? GapAt(local) : -1;

        if (gap < 0 || gap + 1 >= Split.Children.Count)
        {
            base.OnMouseDown(e);
            return;
        }

        _dragGap = gap;
        _dragStart = IsHorizontal ? local.X : local.Y;
        _startA = _extents[gap];
        _startB = _extents[gap + 1];
        _weightA = Split.Children[gap].Weight;
        _weightB = Split.Children[gap + 1].Weight;

        CaptureMouse();
        e.Handled = true;
    }

    /// <summary>The two neighbours trade extent; their weights keep their sum, so the
    /// other children keep theirs.</summary>
    private void DragTo(float position)
    {
        float both = _startA + _startB;

        if (both <= 0) return;

        float min = Math.Min(MinPane, both / 2f);
        float a = Math.Clamp(_startA + position - _dragStart, min, both - min);
        float weights = _weightA + _weightB;

        Split.Children[_dragGap].Weight = weights * a / both;
        Split.Children[_dragGap + 1].Weight = weights - Split.Children[_dragGap].Weight;

        Invalidate();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_dragGap < 0)
        {
            base.OnMouseUp(e);
            return;
        }

        _dragGap = -1;
        ReleaseMouseCapture();
        InvalidateVisual();

        _host.RaiseLayoutChanged();
    }

    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        if (_dragGap < 0)
        {
            base.OnPointerCanceled(e);
            return;
        }

        // nothing was committed: the shares go back to what they were
        if (_dragGap + 1 < Split.Children.Count)
        {
            Split.Children[_dragGap].Weight = _weightA;
            Split.Children[_dragGap + 1].Weight = _weightB;
        }

        _dragGap = -1;
        Invalidate();
    }
}

/// <summary>The edges a floating window is resized by.</summary>
[Flags]
internal enum DockEdges : byte
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
}

/// <summary>A floating window: a title bar, a frame to resize it by, one group inside —
/// over the layout, inside the host.</summary>
internal sealed class DockFloatingWindow : DecoratedPanel
{
    internal const float TitleHeight = 28f;
    private const float Grip = 5f;
    internal const float MinWidth = 160f;
    internal const float MinHeight = 100f;

    private readonly DockHost _host;
    private readonly DockButtons _buttons = new();

    private DockEdges _resizing;
    private Point _resizeStart;
    private Rectangle _boundsStart;

    public DockFloatingWindow(DockHost host, DockFloat window)
    {
        _host = host;
        Float = window;

        _buttons.Set(
            (DockGlyph.Close, () => _host.CloseGroup(Float.Group)),
            (DockGlyph.Unpin, () => _host.RedockGroup(Float.Group)));
    }

    public DockFloat Float { get; }

    private Rectangle Title => new(0, 0, ActualSize.Width, TitleHeight);

    public void Sync()
    {
        if (Float.Group.View is { } view)
            DockHost.SetChildren(this, [view]);

        InvalidateVisual();
    }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        Size size = Float.Bounds.Size;

        foreach (UIElement child in Children)
            child.Measure(new Size(Math.Max(0, size.Width - 2), Math.Max(0, size.Height - TitleHeight - 1)));

        return size;
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        foreach (UIElement child in Children)
            child.Arrange(new Rectangle(1, TitleHeight, Math.Max(0, contentSize.Width - 2), Math.Max(0, contentSize.Height - TitleHeight - 1)), fill: true);
    }

    protected override void DrawContent(Graphics g)
    {
        Rectangle title = Title;

        g.FillRectangle(LocalBounds, _host.PanelBackgroundColor);
        g.FillRectangle(title, _host.ActiveGroup == Float.Group ? _host.ActiveHeaderColor : _host.HeaderColor);

        float buttons = _buttons.Width(title.Height);
        var text = new Rectangle(10, 0, Math.Max(0, title.Width - buttons - 14), title.Height);

        if (Float.Group.Selected is { } item)
        {
            g.Save();
            g.ClipRect(text);
            g.DrawText(item.Title, text, TextColor, EffectiveFont, HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
            g.Restore();
        }

        _buttons.Draw(g, title, TextColor, _host.ButtonHoverColor);
    }

    protected override void DrawDecoration(Graphics g)
    {
        Color border = _host.ActiveGroup == Float.Group ? _host.AccentColor : _host.PanelBorderColor;

        g.DrawRectangle(g.AlignToPixels(LocalBounds), border, 1f);
    }

    private DockEdges EdgesAt(Point local)
    {
        DockEdges edges = DockEdges.None;

        if (local.X < Grip) edges |= DockEdges.Left;
        if (local.X >= ActualSize.Width - Grip) edges |= DockEdges.Right;
        if (local.Y < Grip) edges |= DockEdges.Top;
        if (local.Y >= ActualSize.Height - Grip) edges |= DockEdges.Bottom;

        return edges;
    }

    private static CursorKind CursorFor(DockEdges edges) => edges switch
    {
        DockEdges.Left or DockEdges.Right => CursorKind.SizeWestEast,
        DockEdges.Top or DockEdges.Bottom => CursorKind.SizeNorthSouth,
        DockEdges.Left | DockEdges.Top or DockEdges.Right | DockEdges.Bottom => CursorKind.SizeNorthWestSouthEast,
        DockEdges.Right | DockEdges.Top or DockEdges.Left | DockEdges.Bottom => CursorKind.SizeNorthEastSouthWest,
        _ => CursorKind.Default,
    };

    protected internal override bool HitTestSelfFirst(Point localPoint) =>
        Title.Contains(localPoint) || EdgesAt(localPoint) != DockEdges.None;

    private Point ToLocal(Point location)
    {
        Point at = GetAbsolutePosition();
        return new Point(location.X - at.X, location.Y - at.Y);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e) => _host.ActivateGroup(Float.Group);

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (_resizing != DockEdges.None)
        {
            ResizeTo(e.Location);
            return;
        }

        Point local = ToLocal(e.Location);

        Cursor = CursorFor(EdgesAt(local));

        if (_buttons.Hover(_buttons.IndexAt(Title, local)))
        {
            InvalidateVisual();
            RefreshToolTip();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (_buttons.Hover(-1)) InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.Button != MouseButton.Left)
        {
            base.OnMouseDown(e);
            return;
        }

        Point local = ToLocal(e.Location);
        DockEdges edges = EdgesAt(local);

        if (edges != DockEdges.None)
        {
            _resizing = edges;
            _resizeStart = e.Location;
            _boundsStart = Float.Bounds;

            CaptureMouse();
        }
        else if (_buttons.Press(_buttons.IndexAt(Title, local)))
        {
            // the button acts on release
        }
        else if (Title.Contains(local))
        {
            _host.BeginWindowDrag(Float, e.Location);
        }

        e.Handled = true;
    }

    private void ResizeTo(Point location)
    {
        float dx = location.X - _resizeStart.X;
        float dy = location.Y - _resizeStart.Y;

        Rectangle b = _boundsStart;
        float left = b.X, top = b.Y, right = b.Right, bottom = b.Bottom;

        if (_resizing.HasFlag(DockEdges.Left)) left = Math.Min(left + dx, right - MinWidth);
        if (_resizing.HasFlag(DockEdges.Right)) right = Math.Max(right + dx, left + MinWidth);
        if (_resizing.HasFlag(DockEdges.Top)) top = Math.Min(top + dy, bottom - MinHeight);
        if (_resizing.HasFlag(DockEdges.Bottom)) bottom = Math.Max(bottom + dy, top + MinHeight);

        _host.MoveWindow(Float, new Rectangle(left, top, right - left, bottom - top));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_resizing != DockEdges.None)
        {
            _resizing = DockEdges.None;
            ReleaseMouseCapture();

            _host.RaiseLayoutChanged();
            return;
        }

        if (e.Button == MouseButton.Left && _buttons.Release(_buttons.IndexAt(Title, ToLocal(e.Location))))
            return;

        base.OnMouseUp(e);
    }

    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        _buttons.Cancel();

        if (_resizing == DockEdges.None)
        {
            base.OnPointerCanceled(e);
            return;
        }

        _resizing = DockEdges.None;
        _host.MoveWindow(Float, _boundsStart);
    }

    /// <summary>A double click on the title docks the window back.</summary>
    protected override void OnDoubleClick(MouseClickEventArgs e)
    {
        Point local = ToLocal(e.Location);

        if (!Title.Contains(local) || _buttons.IndexAt(Title, local) >= 0) return;

        _host.RedockGroup(Float.Group);
        e.Handled = true;
    }

    protected internal override string? GetToolTip(Point location)
    {
        int index = _buttons.IndexAt(Title, ToLocal(location));

        return index >= 0 ? DockButtons.ToolTipOf(_buttons.GlyphAt(index)) : base.GetToolTip(location);
    }

    protected override List<MenuItem>? GetDefaultContextMenu() =>
        Float.Group.Selected is { } item && Title.Contains(ToLocal(_host.LastPressLocation)) ? _host.MenuFor(item) : null;
}

/// <summary>An auto-hidden item slid out from its edge: a header with the pin, the
/// content, and the inner edge to resize it by.</summary>
internal sealed class DockAutoHidePanel : DecoratedPanel
{
    internal const float HeaderHeight = 28f;
    private const float Grip = 5f;

    private readonly DockHost _host;
    private readonly DockButtons _buttons = new();

    private bool _resizing;
    private float _resizeStart;
    private float _extentStart;

    public DockAutoHidePanel(DockHost host)
    {
        _host = host;
    }

    public DockItem? Item { get; private set; }

    private Rectangle Header => new(0, 0, ActualSize.Width, HeaderHeight);

    public void Show(DockItem? item)
    {
        Item = item;

        if (item is null)
        {
            _buttons.Set();
            DockHost.SetChildren(this, []);
            return;
        }

        if (item.CanClose)
            _buttons.Set((DockGlyph.Close, () => _host.Close(item)), (DockGlyph.Unpin, () => _host.Redock(item)));
        else
            _buttons.Set((DockGlyph.Unpin, () => _host.Redock(item)));

        DockHost.SetChildren(this, item.Content is { } content ? [content] : []);
        Invalidate();
    }

    private Rectangle ContentRect(Size size)
    {
        Rectangle area = new(0, HeaderHeight, size.Width, Math.Max(0, size.Height - HeaderHeight));

        // the grip is on the edge that faces the layout
        return Item?.Side switch
        {
            DockSide.Left => new Rectangle(area.X, area.Y, Math.Max(0, area.Width - Grip), area.Height),
            DockSide.Right => new Rectangle(area.X + Grip, area.Y, Math.Max(0, area.Width - Grip), area.Height),
            DockSide.Top => new Rectangle(area.X, area.Y, area.Width, Math.Max(0, area.Height - Grip)),
            _ => new Rectangle(area.X, area.Y + Grip, area.Width, Math.Max(0, area.Height - Grip)),
        };
    }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        Rectangle content = ContentRect(availableSize);

        foreach (UIElement child in Children) child.Measure(content.Size);

        return ResolveSize(availableSize, availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        Rectangle content = ContentRect(contentSize);

        // a panel's content fills it, as a window's does
        foreach (UIElement child in Children) child.Arrange(content, fill: true);
    }

    private bool OnGrip(Point local) => Item?.Side switch
    {
        DockSide.Left => local.X >= ActualSize.Width - Grip,
        DockSide.Right => local.X < Grip,
        DockSide.Top => local.Y >= ActualSize.Height - Grip,
        DockSide.Bottom => local.Y < Grip,
        _ => false,
    };

    protected override void DrawContent(Graphics g)
    {
        g.FillRectangle(LocalBounds, _host.PanelBackgroundColor);

        Rectangle header = Header;
        g.FillRectangle(header, _host.ActiveHeaderColor);

        if (Item is { } item)
        {
            var text = new Rectangle(10, 0, Math.Max(0, header.Width - _buttons.Width(header.Height) - 14), header.Height);

            g.Save();
            g.ClipRect(text);
            g.DrawText(item.Title, text, TextColor, EffectiveFont, HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
            g.Restore();
        }

        _buttons.Draw(g, header, TextColor, _host.ButtonHoverColor);
    }

    protected override void DrawDecoration(Graphics g) =>
        g.DrawRectangle(g.AlignToPixels(LocalBounds), _host.PanelBorderColor, 1f);

    protected internal override bool HitTestSelfFirst(Point localPoint) =>
        Header.Contains(localPoint) || OnGrip(localPoint);

    private Point ToLocal(Point location)
    {
        Point at = GetAbsolutePosition();
        return new Point(location.X - at.X, location.Y - at.Y);
    }

    private bool IsHorizontal => Item?.Side is DockSide.Left or DockSide.Right;

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (_resizing && Item is { } item)
        {
            float delta = (IsHorizontal ? e.Location.X : e.Location.Y) - _resizeStart;

            if (item.Side is DockSide.Right or DockSide.Bottom) delta = -delta;

            item.AutoHideExtent = _host.ClampAutoHideExtent(item.Side, _extentStart + delta);
            _host.Invalidate();
            return;
        }

        Point local = ToLocal(e.Location);

        Cursor = OnGrip(local) ? (IsHorizontal ? CursorKind.SizeWestEast : CursorKind.SizeNorthSouth) : CursorKind.Default;

        if (_buttons.Hover(_buttons.IndexAt(Header, local)))
        {
            InvalidateVisual();
            RefreshToolTip();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (_buttons.Hover(-1)) InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.Button != MouseButton.Left || Item is null)
        {
            base.OnMouseDown(e);
            return;
        }

        Point local = ToLocal(e.Location);

        if (OnGrip(local))
        {
            _resizing = true;
            _resizeStart = IsHorizontal ? e.Location.X : e.Location.Y;
            _extentStart = IsHorizontal ? ActualSize.Width : ActualSize.Height;

            CaptureMouse();
        }
        else
        {
            _buttons.Press(_buttons.IndexAt(Header, local));
        }

        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_resizing)
        {
            _resizing = false;
            ReleaseMouseCapture();

            _host.RaiseLayoutChanged();
            return;
        }

        if (e.Button == MouseButton.Left && _buttons.Release(_buttons.IndexAt(Header, ToLocal(e.Location))))
            return;

        base.OnMouseUp(e);
    }

    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        _buttons.Cancel();
        _resizing = false;

        base.OnPointerCanceled(e);
    }

    protected internal override string? GetToolTip(Point location)
    {
        int index = _buttons.IndexAt(Header, ToLocal(location));

        return index >= 0 ? DockButtons.ToolTipOf(_buttons.GlyphAt(index)) : base.GetToolTip(location);
    }
}
