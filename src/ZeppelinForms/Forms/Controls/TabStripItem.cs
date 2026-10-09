using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls;

/// <summary>One tab of a <see cref="TabStrip"/>: a header, nothing more — what it
/// stands for is the application's, through <see cref="Tag"/>.</summary>
public sealed class TabStripItem
{
    internal TabStrip? Owner { get; set; }

    public TabStripItem() { }

    public TabStripItem(string header) => Header = header;

    public string Header
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Owner?.Invalidate();
        }
    } = string.Empty;

    public IconSource? Icon
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Owner?.Invalidate();
        }
    }

    /// <summary>Shows a close button; a middle click and Ctrl+W close it as well.</summary>
    public bool IsClosable
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Owner?.Invalidate();
        }
    } = true;

    public bool IsEnabled
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Owner?.InvalidateVisual();
        }
    } = true;

    /// <summary>The tab's tooltip; without one a header too long for its tab shows in full.</summary>
    public string? ToolTip { get; set; }

    /// <summary>Whatever the application ties to the tab: a document, a page.</summary>
    public object? Tag { get; set; }
}

/// <summary>A tab is about to close. Set <see cref="Cancel"/> to keep it — say, to
/// ask about unsaved changes first.</summary>
public sealed class TabCloseRequestedEventArgs(TabStripItem item) : EventArgs
{
    public TabStripItem Item { get; } = item;

    public bool Cancel { get; set; }
}

/// <summary>A tab changed its place: dragged, or moved from the keyboard.</summary>
public sealed class TabMovedEventArgs(TabStripItem item, int oldIndex, int newIndex) : EventArgs
{
    public TabStripItem Item { get; } = item;

    public int OldIndex { get; } = oldIndex;

    public int NewIndex { get; } = newIndex;
}

/// <summary>
/// A strip of tabs without pages — the tabs of a browser or an editor, where what a
/// tab opens is up to the application. Tabs close, a new one is asked for with the
/// + button, they are reordered by dragging, and when there are too many they first
/// narrow and then scroll.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="TabControl"/> the strip holds no content: switching documents in
/// an editor is the application's business, and a strip that had to own every
/// document's view would keep them all alive.
/// </para>
/// <para>
/// The keyboard: Left and Right select, Home and End go to the ends, Ctrl+W and
/// Ctrl+F4 close the selected tab, Ctrl+Shift+Left and Right move it — reordering
/// without a mouse.
/// </para>
/// </remarks>
public partial class TabStrip : InteractiveControl
{
    private const float PaddingX = 12f;
    private const float PaddingY = 7f;
    private const float IconSize = 14f;
    private const float IconGap = 6f;
    private const float CloseSize = 16f;
    private const float CloseGap = 6f;
    private const float AccentThickness = 3f;

    /// <summary>How far the pointer goes before a press becomes a drag: a click
    /// that wobbles a pixel is still a click.</summary>
    private const float DragThreshold = 4f;

    private readonly List<float> _widths = [];

    private float _scroll;

    /// <summary>The selected tab is to be scrolled into view once the strip knows its
    /// width: a tab selected before the first layout has nothing to be measured against.</summary>
    private bool _revealSelected;

    private int _hovered = -1;
    private bool _hoveredClose;
    private bool _hoveredAdd;

    private enum Press : byte { None, Tab, Close, Add }

    private Press _press;
    private int _pressIndex = -1;
    private float _pressX;
    private bool _dragging;

    /// <summary>The selected tab itself: the selection follows it when tabs are
    /// added, removed or moved, the way <see cref="DataGrid.DataGridView"/>'s does.</summary>
    private TabStripItem? _selected;

    public ObservableCollection<TabStripItem> Items { get; } = [];

    public TabStrip()
    {
        Items.CollectionChanged += OnItemsChanged;

        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
    }

    // ===== selection =====

    public int SelectedIndex
    {
        get => _selected is null ? -1 : Items.IndexOf(_selected);
        set => Select(value >= 0 && value < Items.Count ? Items[value] : null);
    }

    public TabStripItem? SelectedItem
    {
        get => _selected;
        set => Select(value is not null && Items.Contains(value) ? value : null);
    }

    public event EventHandler? SelectionChanged;

    private void Select(TabStripItem? item)
    {
        if (ReferenceEquals(_selected, item)) return;

        _selected = item;
        _revealSelected = true;

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (TabStripItem item in e.OldItems)
                if (!Items.Contains(item)) item.Owner = null;

        if (e.NewItems is not null)
            foreach (TabStripItem item in e.NewItems)
                item.Owner = this;

        // the selected tab closed: its right neighbour takes over, or the left one
        // at the end — the tab that slides into its place, as in a browser
        if (_selected is not null && !Items.Contains(_selected))
        {
            int at = e.Action == NotifyCollectionChangedAction.Remove ? e.OldStartingIndex : 0;
            Select(Items.Count == 0 ? null : Items[Math.Clamp(at, 0, Items.Count - 1)]);
        }

        // the first tab is selected when there was nothing to select before
        if (_selected is null && Items.Count > 0 && e.Action == NotifyCollectionChangedAction.Add)
            Select(Items[0]);

        _hovered = -1;
        Invalidate();
    }

    // ===== options and events =====

    /// <summary>A + button after the last tab raises <see cref="AddTabRequested"/>.</summary>
    public bool ShowAddButton
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    }

    /// <summary>Tabs are dragged to another place.</summary>
    public bool CanReorder { get; set; } = true;

    /// <summary>The narrowest a tab gets before the strip starts to scroll.</summary>
    public float MinTabWidth
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = 80f;

    /// <summary>The widest a tab gets, however long its header.</summary>
    public float MaxTabWidth
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = 220f;

    /// <summary>The + button was pressed. The handler adds the tab — it knows what a new one is.</summary>
    public event EventHandler? AddTabRequested;

    /// <summary>A tab is about to close. Without a cancel it is removed from <see cref="Items"/>.</summary>
    public event EventHandler<TabCloseRequestedEventArgs>? TabCloseRequested;

    public event EventHandler<TabMovedEventArgs>? TabMoved;

    /// <summary>Close a tab as its close button would: <see cref="TabCloseRequested"/>
    /// may keep it. True — it is gone.</summary>
    public bool RequestClose(TabStripItem item)
    {
        if (!Items.Contains(item) || !item.IsClosable) return false;

        var args = new TabCloseRequestedEventArgs(item);
        TabCloseRequested?.Invoke(this, args);

        if (args.Cancel) return false;

        Items.Remove(item);
        return true;
    }

    /// <summary>Move a tab, as a drag would, raising <see cref="TabMoved"/>.</summary>
    public void Move(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Items.Count) return;

        newIndex = Math.Clamp(newIndex, 0, Items.Count - 1);

        if (oldIndex == newIndex) return;

        TabStripItem item = Items[oldIndex];
        Items.Move(oldIndex, newIndex);

        TabMoved?.Invoke(this, new TabMovedEventArgs(item, oldIndex, newIndex));
    }

    // ===== colors =====

    [Styled(Category = "Tabs")]
    public partial Color StripColor { get; set; }
    private static Color StripColorDefault => new(255, 244, 244, 244);

    [Styled(Category = "Tabs")]
    public partial Color TabHoverColor { get; set; }
    private static Color TabHoverColorDefault => new(255, 234, 234, 234);

    [Styled(Category = "Tabs")]
    public partial Color SelectedTabColor { get; set; }
    private static Color SelectedTabColorDefault => Colors.White;

    /// <summary>The stripe under the selected tab.</summary>
    [Styled(Category = "Tabs")]
    public partial Color AccentColor { get; set; }
    private static Color AccentColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    /// <summary>The disk under a hovered close or + button.</summary>
    [Styled(Category = "Tabs")]
    public partial Color ButtonHoverColor { get; set; }
    private static Color ButtonHoverColorDefault => new(40, 0, 0, 0);

    [Styled(Category = "States")]
    public partial Color DisabledTextColor { get; set; }
    private static Color DisabledTextColorDefault => new(255, 165, 165, 165);

    // ===== geometry =====

    private float StripHeight => TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height + PaddingY * 2;

    private float AddButtonWidth => ShowAddButton ? StripHeight : 0f;

    private float NaturalWidth(TabStripItem item)
    {
        float width = PaddingX * 2;

        if (item.Icon is not null) width += IconSize + IconGap;
        if (!string.IsNullOrEmpty(item.Header)) width += TextMeasurer.Current.MeasureText(ApplyTextTransform(item.Header), EffectiveFont).Width;
        if (item.IsClosable) width += CloseGap + CloseSize;

        return Math.Clamp(width, MinTabWidth, Math.Max(MinTabWidth, MaxTabWidth));
    }

    /// <summary>The widths of the tabs: natural, narrowed together toward
    /// <see cref="MinTabWidth"/> when they don't fit; past that the strip scrolls.</summary>
    private void ResolveWidths()
    {
        _widths.Clear();

        float available = TabsArea.Width;
        float total = 0;

        foreach (TabStripItem item in Items)
        {
            float width = NaturalWidth(item);
            _widths.Add(width);
            total += width;
        }

        if (total > available && total > 0)
        {
            // narrow the wide ones first: each tab gives up what it has above a common
            // width, the way browser tabs shrink to the same size
            float target = Math.Max(MinTabWidth, available / Math.Max(1, Items.Count));

            for (int i = 0; i < _widths.Count; i++)
                _widths[i] = Math.Min(_widths[i], Math.Max(target, MinTabWidth));
        }

        // the scroll is always within what the widths allow now: a tab closed at
        // the end must not leave an empty stretch
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);

        if (_revealSelected && available > 0)
        {
            _revealSelected = false;
            Reveal(SelectedIndex);
        }
    }

    /// <summary>Scroll so that a tab is whole in view, from the widths just resolved.</summary>
    private void Reveal(int index)
    {
        if (index < 0 || index >= _widths.Count) return;

        Rectangle area = TabsArea;
        float left = 0;

        for (int i = 0; i < index; i++) left += _widths[i];

        float right = left + _widths[index];

        if (left < _scroll) _scroll = left;
        else if (right > _scroll + area.Width) _scroll = right - area.Width;

        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
    }

    /// <summary>Where the tabs go: the content less the + button.</summary>
    private Rectangle TabsArea
    {
        get
        {
            Rectangle content = ContentBounds;
            return new Rectangle(content.Position, new Size(Math.Max(0, content.Width - AddButtonWidth), Math.Min(content.Height, StripHeight)));
        }
    }

    private float TotalWidth
    {
        get
        {
            float total = 0;
            foreach (float width in _widths) total += width;
            return total;
        }
    }

    private float MaxScroll => Math.Max(0, TotalWidth - TabsArea.Width);

    // internal for the accessibility peer: tabs are not elements
    internal Rectangle TabRect(int index)
    {
        if (_widths.Count != Items.Count || _revealSelected) ResolveWidths();

        Rectangle area = TabsArea;
        float x = area.X - _scroll;

        for (int i = 0; i < index; i++) x += _widths[i];

        return new Rectangle(new Point(x, area.Y), new Size(index < _widths.Count ? _widths[index] : 0, area.Height));
    }

    private Rectangle CloseRect(Rectangle tab) =>
        new(new Point(tab.X + tab.Width - PaddingX / 2f - CloseSize, tab.Y + (tab.Height - CloseSize) / 2f), new Size(CloseSize, CloseSize));

    /// <summary>The + button: right after the last tab, or at the strip's end once
    /// the tabs scroll — it must not scroll away with them.</summary>
    private Rectangle AddRect
    {
        get
        {
            Rectangle area = TabsArea;
            float x = Math.Min(area.X + TotalWidth - _scroll, area.X + area.Width);

            return new Rectangle(new Point(x, area.Y), new Size(AddButtonWidth, area.Height));
        }
    }

    private static bool Contains(Rectangle r, Point p) =>
        p.X >= r.X && p.X < r.X + r.Width && p.Y >= r.Y && p.Y < r.Y + r.Height;

    private Point ToLocal(Point location)
    {
        Point abs = GetAbsolutePosition();
        return new Point(location.X - abs.X, location.Y - abs.Y);
    }

    private int IndexAt(Point local)
    {
        Rectangle area = TabsArea;

        // the tabs scrolled under the + button are not under the pointer
        if (local.X < area.X || local.X >= area.X + area.Width) return -1;

        for (int i = 0; i < Items.Count; i++)
            if (Contains(TabRect(i), local)) return i;

        return -1;
    }

    // ===== drawing =====

    protected override void DrawContent(Graphics g)
    {
        ResolveWidths();

        Rectangle area = TabsArea;
        Font font = EffectiveFont;

        g.FillRectangle(new Rectangle(ContentBounds.Position, new Size(ContentBounds.Width, area.Height)), StripColor);

        g.Save();
        g.ClipRect(area);

        for (int i = 0; i < Items.Count; i++)
        {
            Rectangle tab = TabRect(i);

            if (tab.X + tab.Width < area.X || tab.X > area.X + area.Width) continue;

            DrawTab(g, Items[i], i, tab, font);
        }

        g.Restore();

        if (ShowAddButton)
        {
            Rectangle add = AddRect;
            var square = new Rectangle(
                new Point(add.X + (add.Width - CloseSize - 6) / 2f, add.Y + (add.Height - CloseSize - 6) / 2f),
                new Size(CloseSize + 6, CloseSize + 6));

            if (_hoveredAdd) g.FillEllipse(square, ButtonHoverColor);

            DrawPlus(g, square, TextColor);
        }
    }

    private void DrawTab(Graphics g, TabStripItem item, int index, Rectangle tab, Font font)
    {
        bool selected = ReferenceEquals(item, _selected);
        bool hovered = index == _hovered && item.IsEnabled;

        if (selected) g.FillRectangle(tab, SelectedTabColor);
        else if (hovered) g.FillRectangle(tab, TabHoverColor);

        if (selected)
        {
            g.FillRectangle(
                new Rectangle(new Point(tab.X, tab.Y + tab.Height - AccentThickness), new Size(tab.Width, AccentThickness)),
                AccentColor);

            // the keyboard's tab: an outline inside it while focus is visible
            if (IsFocusVisible)
                g.DrawRectangle(
                    new Rectangle(new Point(tab.X + 2, tab.Y + 2), new Size(Math.Max(0, tab.Width - 4), Math.Max(0, tab.Height - 4))),
                    AccentColor, 1f);
        }

        Color text = item.IsEnabled ? TextColor : DisabledTextColor;
        float x = tab.X + PaddingX;
        float right = tab.X + tab.Width - PaddingX;

        if (item.Icon is not null)
        {
            item.Icon.Draw(g, new Rectangle(new Point(x, tab.Y + (tab.Height - IconSize) / 2f), new Size(IconSize, IconSize)), text);
            x += IconSize + IconGap;
        }

        if (item.IsClosable)
        {
            Rectangle close = CloseRect(tab);
            right = close.X - CloseGap;

            if (hovered && _hoveredClose) g.FillEllipse(close, ButtonHoverColor);

            DrawCross(g, close, text);
        }

        if (!string.IsNullOrEmpty(item.Header))
        {
            var area = new Rectangle(new Point(x, tab.Y), new Size(Math.Max(0, right - x), tab.Height));

            g.Save();
            g.ClipRect(area);
            g.DrawText(ApplyTextTransform(item.Header), area, text, font, HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
            g.Restore();
        }
    }

    private static void DrawCross(Graphics g, Rectangle square, Color color)
    {
        float inset = square.Width * 0.3f;

        g.DrawLine(new Point(square.X + inset, square.Y + inset), new Point(square.X + square.Width - inset, square.Y + square.Height - inset), color, 1.3f);
        g.DrawLine(new Point(square.X + square.Width - inset, square.Y + inset), new Point(square.X + inset, square.Y + square.Height - inset), color, 1.3f);
    }

    private static void DrawPlus(Graphics g, Rectangle square, Color color)
    {
        float inset = square.Width * 0.3f;
        float cx = square.X + square.Width / 2f;
        float cy = square.Y + square.Height / 2f;

        g.DrawLine(new Point(square.X + inset, cy), new Point(square.X + square.Width - inset, cy), color, 1.5f);
        g.DrawLine(new Point(cx, square.Y + inset), new Point(cx, square.Y + square.Height - inset), color, 1.5f);
    }

    // ===== mouse =====

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        Point local = ToLocal(e.Location);

        if (_press == Press.Tab && CanReorder && _pressIndex >= 0)
        {
            if (!_dragging && Math.Abs(e.Location.X - _pressX) >= DragThreshold)
                _dragging = true;

            if (_dragging)
            {
                DragTo(local.X);
                return;
            }
        }

        int hovered = IndexAt(local);
        bool overClose = hovered >= 0 && Items[hovered].IsClosable && Contains(CloseRect(TabRect(hovered)), local);
        bool overAdd = ShowAddButton && Contains(AddRect, local);

        if (hovered == _hovered && overClose == _hoveredClose && overAdd == _hoveredAdd) return;

        _hovered = hovered;
        _hoveredClose = overClose;
        _hoveredAdd = overAdd;

        InvalidateVisual();

        // another tab, or a button of one, under the pointer: its tooltip
        RefreshToolTip();
    }

    /// <summary>The dragged tab trades places with a neighbour once the pointer
    /// passes that neighbour's middle: past the edge alone, two tabs of different
    /// widths would swap back and forth under a still pointer. A fast drag passes
    /// several tabs in one move, and the tab follows past all of them.</summary>
    private void DragTo(float x)
    {
        while (true)
        {
            int index = _pressIndex;

            if (index + 1 < Items.Count)
            {
                Rectangle next = TabRect(index + 1);

                if (x > next.X + next.Width / 2f)
                {
                    Move(index, index + 1);
                    _pressIndex = index + 1;
                    continue;
                }
            }

            if (index > 0)
            {
                Rectangle previous = TabRect(index - 1);

                if (x < previous.X + previous.Width / 2f)
                {
                    Move(index, index - 1);
                    _pressIndex = index - 1;
                    continue;
                }
            }

            return;
        }
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (_hovered < 0 && !_hoveredAdd) return;

        _hovered = -1;
        _hoveredClose = false;
        _hoveredAdd = false;
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Point local = ToLocal(e.Location);
        int index = IndexAt(local);

        _press = Press.None;
        _dragging = false;

        // the middle button closes, as in every browser
        if (e.Button == MouseButton.Middle)
        {
            if (index >= 0) RequestClose(Items[index]);
            return;
        }

        if (e.Button != MouseButton.Left) return;

        if (ShowAddButton && Contains(AddRect, local))
        {
            _press = Press.Add;
            return;
        }

        if (index < 0 || !Items[index].IsEnabled) return;

        if (Items[index].IsClosable && Contains(CloseRect(TabRect(index)), local))
        {
            _press = Press.Close;
            _pressIndex = index;
            return;
        }

        // selected on press, not on release: a tab is a switch, not a button
        SelectedIndex = index;

        _press = Press.Tab;
        _pressIndex = index;
        _pressX = e.Location.X;

        // without capture a drag breaks off as soon as the cursor leaves the strip
        CaptureMouse();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        Press press = _press;
        int index = _pressIndex;
        Point local = ToLocal(e.Location);

        if (press == Press.Tab) ReleaseMouseCapture();

        _press = Press.None;
        _pressIndex = -1;
        _dragging = false;

        switch (press)
        {
            // the button acts only if the release lands on it too
            case Press.Close when index < Items.Count && IndexAt(local) == index && Contains(CloseRect(TabRect(index)), local):
                RequestClose(Items[index]);
                break;

            case Press.Add when Contains(AddRect, local):
                AddTabRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        _press = Press.None;
        _pressIndex = -1;
        _dragging = false;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!IsEnabled || MaxScroll <= 0) return;

        float before = _scroll;
        _scroll = Math.Clamp(_scroll - e.Delta / 120f * MinTabWidth / 2f, 0, MaxScroll);

        if (_scroll == before) return;

        e.Handled = true;
        InvalidateVisual();
    }

    /// <summary>The buttons say what they do; a tab shows its own tooltip, or its header
    /// when the tab is too narrow to show it.</summary>
    protected internal override string? GetToolTip(Point location)
    {
        Point local = ToLocal(location);

        if (ShowAddButton && Contains(AddRect, local))
            return Localization.Get(ZfText.NewTab);

        int index = IndexAt(local);

        if (index < 0) return base.GetToolTip(location);

        TabStripItem item = Items[index];

        if (item.IsClosable && Contains(CloseRect(TabRect(index)), local))
            return Localization.Get(ZfText.CloseTab);

        if (item.ToolTip is { Length: > 0 } tip) return tip;

        return NaturalWidth(item) > TabRect(index).Width + 0.5f || NaturalWidth(item) >= MaxTabWidth
            ? item.Header
            : base.GetToolTip(location);
    }

    // ===== keyboard =====

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = e.Modifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.Modifiers.HasFlag(KeyModifiers.Shift);
        int index = SelectedIndex;

        switch (e.Key)
        {
            case Key.Left when ctrl && shift:
                if (index > 0) Move(index, index - 1);
                break;

            case Key.Right when ctrl && shift:
                if (index >= 0 && index < Items.Count - 1) Move(index, index + 1);
                break;

            case Key.Left:
                if (!SelectNextEnabled(-1)) return;
                break;

            case Key.Right:
                if (!SelectNextEnabled(1)) return;
                break;

            case Key.Home:
                SelectFirstEnabled(fromEnd: false);
                break;

            case Key.End:
                SelectFirstEnabled(fromEnd: true);
                break;

            case Key.W or Key.F4 when ctrl:
                if (_selected is null) return;
                RequestClose(_selected);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private bool SelectNextEnabled(int step)
    {
        for (int i = SelectedIndex + step; i >= 0 && i < Items.Count; i += step)
        {
            if (!Items[i].IsEnabled) continue;

            SelectedIndex = i;
            return true;
        }

        return false;
    }

    private void SelectFirstEnabled(bool fromEnd)
    {
        for (int n = 0; n < Items.Count; n++)
        {
            int i = fromEnd ? Items.Count - 1 - n : n;

            if (!Items[i].IsEnabled) continue;

            SelectedIndex = i;
            return;
        }
    }

    // ===== layout =====

    protected override Size MeasureOverride(Size availableSize)
    {
        float width = AddButtonWidth;

        foreach (TabStripItem item in Items)
            width += NaturalWidth(item);

        return ResolveSize(new Size(width + Padding.Horizontal, StripHeight + Padding.Vertical), availableSize);
    }
}