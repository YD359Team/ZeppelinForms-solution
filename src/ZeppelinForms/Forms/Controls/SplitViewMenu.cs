using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// The pane a <see cref="SplitView"/> builds from its <see cref="SplitView.Items"/>:
/// the back and pane buttons, the entries, the footer entries — drawn by this one
/// control, as a <see cref="TabStrip"/> draws its tabs.
/// </summary>
/// <remarks>
/// <para>
/// It is laid out at the pane's open width, like any pane, and the compact strip is
/// its edge: the icons stand in a column as wide as
/// <see cref="SplitView.CompactPaneLength"/> at the pane's outer side, the texts
/// beside it, hidden while the pane is closed.
/// </para>
/// <para>
/// The keyboard: Up and Down move through the buttons and the entries, Home and End
/// to the ends, Enter and Space choose. The entries scroll with the wheel when there
/// are more than fit; the footer entries stay at the bottom.
/// </para>
/// </remarks>
internal sealed class SplitViewMenu : InteractiveControl
{
    private const float IconSize = 16f;
    private const float RowInset = 4f;
    private const float RowRadius = 4f;
    private const float IndicatorThickness = 3f;
    private const float IndicatorLength = 16f;
    private const float SeparatorHeight = 9f;
    private const float TextPadding = 8f;
    private const float BadgeHeight = 16f;
    private const float BadgePadding = 6f;
    private const float BadgeDot = 8f;

    internal enum RowKind : byte { Back, Toggle, Item, Header, Separator }

    /// <summary>One line of the menu, where it is now.</summary>
    internal readonly record struct MenuRow(RowKind Kind, SplitViewItem? Item, Rectangle Bounds, bool InFooter);

    private readonly SplitView _view;

    private float _scroll;

    /// <summary>The row under the pointer, the one pressed, the keyboard's: indices
    /// into <see cref="Rows"/>, −1 — none.</summary>
    private int _hovered = -1;
    private int _pressed = -1;
    private int _cursor = -1;

    public SplitViewMenu(SplitView view)
    {
        _view = view;

        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        SetControlDefault(VerticalAlignmentProperty, VerticalAlignment.Stretch);
    }

    internal SplitView View => _view;

    // ===== geometry =====

    private float RowHeight => Math.Max(16f, _view.ItemHeight);

    private float HeaderHeight => RowHeight * 0.8f;

    private float ColumnWidth => Math.Max(IconSize, _view.CompactPaneLength);

    /// <summary>The icon column is at the pane's outer edge: on the right when the
    /// pane is. Its compact strip must show the icons, not the texts' ends.</summary>
    private bool ColumnAtRight => _view.PaneAtRight;

    private float Height(SplitViewItem item) => item.Kind switch
    {
        SplitViewItemKind.Header => HeaderHeight,
        SplitViewItemKind.Separator => SeparatorHeight,
        _ => RowHeight,
    };

    private float TopHeight =>
        (_view.IsBackButtonVisible ? RowHeight : 0f) + (_view.IsPaneToggleButtonVisible ? RowHeight : 0f);

    private float FooterHeight =>
        _view.FooterItems.Count == 0 ? 0f : SeparatorHeight + _view.FooterItems.Sum(Height);

    /// <summary>Where the scrolled entries are: between the buttons and the footer.</summary>
    private Rectangle ItemsArea
    {
        get
        {
            float top = TopHeight;
            float bottom = Math.Max(top, ActualSize.Height - FooterHeight);

            return new Rectangle(new Point(0, top), new Size(ActualSize.Width, bottom - top));
        }
    }

    private float ItemsHeight => _view.Items.Sum(Height);

    private float MaxScroll => Math.Max(0, ItemsHeight - ItemsArea.Height);

    /// <summary>Every row, top to bottom, where it is now. Entries scrolled out of
    /// their area are left out: they can be neither seen nor hit.</summary>
    internal List<MenuRow> Rows
    {
        get
        {
            var rows = new List<MenuRow>();
            float width = ActualSize.Width;
            float y = 0;

            if (_view.IsBackButtonVisible)
            {
                rows.Add(new MenuRow(RowKind.Back, null, new Rectangle(new Point(0, y), new Size(width, RowHeight)), false));
                y += RowHeight;
            }

            if (_view.IsPaneToggleButtonVisible)
            {
                rows.Add(new MenuRow(RowKind.Toggle, null, new Rectangle(new Point(0, y), new Size(width, RowHeight)), false));
                y += RowHeight;
            }

            Rectangle area = ItemsArea;
            _scroll = Math.Clamp(_scroll, 0, MaxScroll);
            y = area.Y - _scroll;

            foreach (SplitViewItem item in _view.Items)
            {
                float height = Height(item);
                var bounds = new Rectangle(new Point(0, y), new Size(width, height));
                y += height;

                // wholly outside the area: neither drawn nor hit
                if (bounds.Y + bounds.Height <= area.Y || bounds.Y >= area.Y + area.Height) continue;

                rows.Add(new MenuRow(Kind(item), item, bounds, false));
            }

            y = ActualSize.Height - FooterHeight + SeparatorHeight;

            foreach (SplitViewItem item in _view.FooterItems)
            {
                float height = Height(item);
                rows.Add(new MenuRow(Kind(item), item, new Rectangle(new Point(0, y), new Size(width, height)), true));
                y += height;
            }

            return rows;
        }
    }

    private static RowKind Kind(SplitViewItem item) => item.Kind switch
    {
        SplitViewItemKind.Header => RowKind.Header,
        SplitViewItemKind.Separator => RowKind.Separator,
        _ => RowKind.Item,
    };

    /// <summary>Whether the row does anything when chosen.</summary>
    private bool IsActive(MenuRow row) => row.Kind switch
    {
        RowKind.Back => _view.CanGoBack,
        RowKind.Toggle => true,
        RowKind.Item => row.Item!.IsChoosable,
        _ => false,
    };

    private static Rectangle RowBackground(Rectangle row) =>
        new(new Point(row.X + RowInset, row.Y + 2f), new Size(Math.Max(0, row.Width - 2 * RowInset), Math.Max(0, row.Height - 4f)));

    private Rectangle IconRect(Rectangle row)
    {
        float column = ColumnAtRight ? row.X + row.Width - ColumnWidth : row.X;

        return new Rectangle(
            new Point(column + (ColumnWidth - IconSize) / 2f, row.Y + (row.Height - IconSize) / 2f),
            new Size(IconSize, IconSize));
    }

    /// <summary>Where a row's text goes: beside the icon column, less the badge.</summary>
    private Rectangle TextRect(Rectangle row, float badge = 0f)
    {
        float start = ColumnAtRight ? row.X + RowInset + TextPadding + badge : row.X + ColumnWidth;
        float end = ColumnAtRight ? row.X + row.Width - ColumnWidth : row.X + row.Width - RowInset - TextPadding - badge;

        return new Rectangle(new Point(start, row.Y), new Size(Math.Max(0, end - start), row.Height));
    }

    private HorizontalContentAlignment TextAlign =>
        IsRightToLeft ? HorizontalContentAlignment.Right : HorizontalContentAlignment.Left;

    private static bool Contains(Rectangle r, Point p) =>
        p.X >= r.X && p.X < r.X + r.Width && p.Y >= r.Y && p.Y < r.Y + r.Height;

    private Point ToLocal(Point location)
    {
        Point abs = GetAbsolutePosition();
        return new Point(location.X - abs.X, location.Y - abs.Y);
    }

    private int RowAt(Point local)
    {
        List<MenuRow> rows = Rows;
        Rectangle area = ItemsArea;

        for (int i = 0; i < rows.Count; i++)
        {
            MenuRow row = rows[i];

            // a half-scrolled entry is hit only where it shows
            if (row.Item is not null && !row.InFooter && (local.Y < area.Y || local.Y >= area.Y + area.Height)) continue;

            if (Contains(row.Bounds, local)) return i;
        }

        return -1;
    }

    // ===== drawing =====

    protected override void DrawContent(Graphics g)
    {
        List<MenuRow> rows = Rows;
        Rectangle area = ItemsArea;

        for (int i = 0; i < rows.Count; i++)
        {
            MenuRow row = rows[i];
            bool scrolled = row.Item is not null && !row.InFooter;

            if (scrolled)
            {
                g.Save();
                g.ClipRect(area);
            }

            DrawRow(g, row, i);

            if (scrolled) g.Restore();
        }

        if (_view.FooterItems.Count > 0)
        {
            float y = ActualSize.Height - FooterHeight + SeparatorHeight / 2f;
            DrawLine(g, y);
        }
    }

    private void DrawRow(Graphics g, MenuRow row, int index)
    {
        switch (row.Kind)
        {
            case RowKind.Separator:
                DrawLine(g, row.Bounds.Y + row.Bounds.Height / 2f);
                return;

            case RowKind.Header:
                DrawHeader(g, row);
                return;
        }

        bool active = IsActive(row) && IsEffectivelyEnabled;
        bool selected = row.Kind == RowKind.Item && ReferenceEquals(row.Item, _view.SelectedItem);
        Rectangle back = RowBackground(row.Bounds);

        Color fill = index == _pressed && active ? _view.ItemPressedColor
            : selected ? _view.SelectedItemColor
            : index == _hovered && active ? _view.ItemHoverColor
            : Colors.Transparent;

        if (fill.A > 0) g.FillRoundRectangle(back, new CornerRadius(RowRadius), fill);

        if (selected)
        {
            float x = ColumnAtRight ? back.X + back.Width - IndicatorThickness : back.X;
            var indicator = new Rectangle(
                new Point(x, back.Y + (back.Height - IndicatorLength) / 2f),
                new Size(IndicatorThickness, IndicatorLength));

            g.FillRoundRectangle(indicator, new CornerRadius(IndicatorThickness / 2f), _view.SelectionIndicatorColor);
        }

        Color text = active ? TextColor : _view.DisabledTextColor;
        Rectangle icon = IconRect(row.Bounds);

        switch (row.Kind)
        {
            case RowKind.Back:
                DrawBackArrow(g, icon, text);
                break;

            case RowKind.Toggle:
                DrawHamburger(g, icon, text);

                if (_view.PaneTitle is { Length: > 0 } title)
                    g.DrawTextLine(ApplyTextTransform(title), TextRect(row.Bounds), text, EffectiveFont.SemiBold(), TextAlign);
                break;

            case RowKind.Item:
                DrawItem(g, row, icon, text);
                break;
        }

        if (index == _cursor && IsFocused)
            DrawFocusRing(g, back, new CornerRadius(RowRadius), _view.SelectionIndicatorColor);
    }

    private void DrawItem(Graphics g, MenuRow row, Rectangle icon, Color text)
    {
        SplitViewItem item = row.Item!;

        item.Icon?.Draw(g, icon, text);

        float badge = 0f;

        if (item.Badge is { Length: > 0 } mark)
        {
            Font small = EffectiveFont.WithSize(EffectiveFont.Size * 0.8f);
            float width = Math.Max(BadgeHeight, TextMeasurer.Current.MeasureText(mark, small).Width + 2 * BadgePadding);
            Rectangle bounds = row.Bounds;

            float x = ColumnAtRight ? bounds.X + RowInset + TextPadding : bounds.X + bounds.Width - RowInset - TextPadding - width;
            var pill = new Rectangle(new Point(x, bounds.Y + (bounds.Height - BadgeHeight) / 2f), new Size(width, BadgeHeight));

            g.FillRoundRectangle(pill, new CornerRadius(BadgeHeight / 2f), _view.BadgeColor);
            g.DrawTextLine(mark, pill, _view.BadgeTextColor, small, HorizontalContentAlignment.Center);

            badge = width + TextPadding;

            // the strip of a closed pane hides the pill: a dot on the icon says it is there
            if (!_view.IsPaneOpen && _view.OpenProgress < 1f)
            {
                float dotX = icon.X + icon.Width - BadgeDot / 2f;
                var dot = new Rectangle(new Point(dotX, icon.Y - BadgeDot / 2f), new Size(BadgeDot, BadgeDot));
                g.FillEllipse(dot, _view.BadgeColor);
            }
        }

        if (item.Text.Length > 0)
            g.DrawTextLine(ApplyTextTransform(item.Text), TextRect(row.Bounds, badge), text, EffectiveFont, TextAlign);
    }

    private void DrawHeader(Graphics g, MenuRow row)
    {
        Rectangle bounds = row.Bounds;

        // aligned with the entries' texts, so the strip of a closed pane hides it;
        // the strip shows a line in its place, fading out as the pane opens
        if (row.Item!.Text.Length > 0)
            g.DrawTextLine(ApplyTextTransform(row.Item.Text), TextRect(bounds), _view.HeaderTextColor, EffectiveFont.SemiBold(), TextAlign);

        float fade = 1f - Math.Clamp(_view.OpenProgress, 0f, 1f);

        if (fade > 0f && _view.IsCompact)
        {
            Color line = _view.SeparatorColor;
            var faded = new Color((byte)(line.A * fade), line.R, line.G, line.B);
            float column = ColumnAtRight ? bounds.X + bounds.Width - ColumnWidth : bounds.X;

            g.FillRectangle(
                new Rectangle(new Point(column + 12f, bounds.Y + bounds.Height / 2f), new Size(Math.Max(0, ColumnWidth - 24f), 1f)),
                faded);
        }
    }

    private void DrawLine(Graphics g, float y) =>
        g.FillRectangle(
            new Rectangle(new Point(RowInset + 8f, y), new Size(Math.Max(0, ActualSize.Width - 2 * (RowInset + 8f)), 1f)),
            _view.SeparatorColor);

    private static void DrawHamburger(Graphics g, Rectangle icon, Color color)
    {
        for (int i = 0; i < 3; i++)
        {
            float y = icon.Y + 3f + i * 5f;
            g.DrawLine(new Point(icon.X + 1f, y), new Point(icon.X + icon.Width - 1f, y), color, 1.5f);
        }
    }

    private void DrawBackArrow(Graphics g, Rectangle icon, Color color)
    {
        // back is toward where lines start: the left, the right under right-to-left
        float dir = IsRightToLeft ? -1f : 1f;
        float cx = icon.X + icon.Width / 2f;
        float cy = icon.Y + icon.Height / 2f;
        float half = icon.Width / 2f - 1f;

        var tip = new Point(cx - dir * half, cy);

        g.DrawLine(tip, new Point(cx + dir * half, cy), color, 1.5f);
        g.DrawLine(tip, new Point(tip.X + dir * 6f, cy - 6f), color, 1.5f);
        g.DrawLine(tip, new Point(tip.X + dir * 6f, cy + 6f), color, 1.5f);
    }

    // ===== state =====

    internal void OnItemsChanged()
    {
        _hovered = _pressed = -1;
        _cursor = Math.Min(_cursor, Rows.Count - 1);
        Invalidate();
    }

    internal void OnSelectionChanged()
    {
        Reveal(_view.SelectedItem);
        InvalidateVisual();
    }

    /// <summary>Scroll an entry into view.</summary>
    private void Reveal(SplitViewItem? item)
    {
        int index = item is null ? -1 : _view.Items.IndexOf(item);
        if (index < 0) return;

        float top = _view.Items.Take(index).Sum(Height);
        float bottom = top + Height(item!);
        float visible = ItemsArea.Height;

        if (visible <= 0) return;

        if (top < _scroll) _scroll = top;
        else if (bottom > _scroll + visible) _scroll = bottom - visible;

        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
    }

    // ===== mouse =====

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        int hovered = RowAt(ToLocal(e.Location));
        if (hovered == _hovered) return;

        _hovered = hovered;
        InvalidateVisual();

        // another row under the pointer: its tooltip
        RefreshToolTip();
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (_hovered < 0) return;

        _hovered = -1;
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.Button != MouseButton.Left) return;

        int index = RowAt(ToLocal(e.Location));
        List<MenuRow> rows = Rows;

        if (index < 0 || !IsActive(rows[index])) return;

        _pressed = index;
        _cursor = index;

        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        int pressed = _pressed;
        if (pressed < 0) return;

        _pressed = -1;
        ReleaseMouseCapture();
        InvalidateVisual();

        // a button acts only if the release lands on the row it was pressed on
        if (RowAt(ToLocal(e.Location)) != pressed) return;

        List<MenuRow> rows = Rows;
        if (pressed < rows.Count) Activate(rows[pressed]);
    }

    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        if (_pressed < 0) return;

        _pressed = -1;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (MaxScroll <= 0) return;

        float before = _scroll;
        _scroll = Math.Clamp(_scroll - e.Delta / 120f * RowHeight, 0, MaxScroll);

        if (_scroll == before) return;

        e.Handled = true;
        _hovered = RowAt(ToLocal(e.Location));
        InvalidateVisual();
    }

    private void Activate(MenuRow row)
    {
        if (!IsActive(row) || !IsEffectivelyEnabled) return;

        switch (row.Kind)
        {
            case RowKind.Back:
                _view.GoBack();
                break;

            case RowKind.Toggle:
                _view.TogglePane();
                break;

            case RowKind.Item:
                _view.Invoke(row.Item!);
                break;
        }

        InvalidateVisual();
    }

    /// <summary>The buttons say what they do; an entry shows its tooltip, or its text
    /// while the pane is closed and the text hidden.</summary>
    protected internal override string? GetToolTip(Point location)
    {
        int index = RowAt(ToLocal(location));
        if (index < 0) return base.GetToolTip(location);

        MenuRow row = Rows[index];

        return row.Kind switch
        {
            RowKind.Back => Localization.Get(ZfText.Back),
            RowKind.Toggle => Localization.Get(_view.IsPaneOpen ? ZfText.CloseNavigation : ZfText.OpenNavigation),
            RowKind.Item when row.Item!.ToolTip is { Length: > 0 } tip => tip,
            RowKind.Item when !_view.IsPaneOpen && row.Item!.Text.Length > 0 => row.Item.Text,
            _ => base.GetToolTip(location),
        };
    }

    // ===== keyboard =====

    /// <summary>The row the keyboard is on, for the accessibility tree; −1 — none.</summary>
    internal int KeyboardRow => _cursor;

    protected override void OnGotFocus()
    {
        base.OnGotFocus();

        // the keyboard starts on the selected entry, or on the first row that acts
        List<MenuRow> rows = Rows;

        if (_cursor < 0 || _cursor >= rows.Count || !IsActive(rows[_cursor]))
        {
            int selected = rows.FindIndex(r => r.Kind == RowKind.Item && ReferenceEquals(r.Item, _view.SelectedItem));
            _cursor = selected >= 0 ? selected : rows.FindIndex(IsActive);
        }

        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Modifiers != KeyModifiers.None)
        {
            base.OnKeyDown(e);
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                if (!MoveCursor(1)) return;
                break;

            case Key.Up:
                if (!MoveCursor(-1)) return;
                break;

            case Key.Home:
                _cursor = -1;
                MoveCursor(1);
                break;

            case Key.End:
                _cursor = int.MaxValue;
                MoveCursor(-1);
                break;

            case Key.Enter or Key.Space:
                List<MenuRow> rows = Rows;
                if (_cursor < 0 || _cursor >= rows.Count) return;
                Activate(rows[_cursor]);
                break;

            default:
                base.OnKeyDown(e);
                return;
        }

        e.Handled = true;
    }

    /// <summary>To the next row that acts, scrolling it into view. False — there is none.</summary>
    private bool MoveCursor(int step)
    {
        // all the entries, scrolled out or not: the cursor goes through every one
        var order = new List<(RowKind Kind, SplitViewItem? Item)>();

        if (_view.IsBackButtonVisible) order.Add((RowKind.Back, null));
        if (_view.IsPaneToggleButtonVisible) order.Add((RowKind.Toggle, null));

        foreach (SplitViewItem item in _view.Items) order.Add((Kind(item), item));
        foreach (SplitViewItem item in _view.FooterItems) order.Add((Kind(item), item));

        List<MenuRow> rows = Rows;
        int current = _cursor >= 0 && _cursor < rows.Count
            ? order.FindIndex(o => o.Kind == rows[_cursor].Kind && ReferenceEquals(o.Item, rows[_cursor].Item))
            : _cursor >= int.MaxValue ? order.Count : -1;

        for (int i = current + step; i >= 0 && i < order.Count; i += step)
        {
            (RowKind kind, SplitViewItem? item) = order[i];

            bool active = kind switch
            {
                RowKind.Back => _view.CanGoBack,
                RowKind.Toggle => true,
                RowKind.Item => item!.IsChoosable,
                _ => false,
            };

            if (!active) continue;

            if (item is not null) Reveal(item);

            rows = Rows;
            _cursor = rows.FindIndex(r => r.Kind == kind && ReferenceEquals(r.Item, item));
            InvalidateVisual();
            return true;
        }

        return false;
    }

    // ===== layout =====

    protected override Size MeasureOverride(Size availableSize)
    {
        float height = TopHeight + ItemsHeight + FooterHeight;

        return new Size(
            float.IsFinite(availableSize.Width) ? availableSize.Width : _view.OpenPaneLength,
            float.IsFinite(availableSize.Height) ? availableSize.Height : height);
    }
}

/// <summary>
/// The pane button of a menu whose pane hides whole while closed — the Overlay and
/// Inline modes. It stands in a band above the content, on the pane's side, where the
/// menu's own pane button is when the pane is open; it hides while the pane moves.
/// </summary>
/// <remarks>
/// <para>
/// Without it a split view with <see cref="SplitView.Items"/> in the default Overlay
/// mode had nothing to open its menu with: the pane, and the button in it, were
/// hidden. WinUI's navigation view keeps the same button in its minimal mode.
/// </para>
/// <para>
/// In 0.15.0 it stood over the content's corner and covered what was there — the
/// first header of a grid, the hover fill coming and going over it. Since 0.15.1 the
/// content is laid out under the button's band.
/// </para>
/// </remarks>
internal sealed class SplitViewPaneButton : InteractiveControl
{
    private readonly SplitView _view;

    public SplitViewPaneButton(SplitView view)
    {
        _view = view;

        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        SetControlDefault(VerticalAlignmentProperty, VerticalAlignment.Stretch);
    }

    protected override void DrawContent(Graphics g)
    {
        Rectangle bounds = LocalBounds;
        var back = new Rectangle(new Point(4f, 2f), new Size(Math.Max(0, bounds.Width - 8f), Math.Max(0, bounds.Height - 4f)));

        Color fill = IsPressed ? _view.ItemPressedColor : IsHovered ? _view.ItemHoverColor : Colors.Transparent;
        if (fill.A > 0) g.FillRoundRectangle(back, new CornerRadius(4f), fill);

        const float size = 16f;
        float x = (bounds.Width - size) / 2f;
        float y = (bounds.Height - size) / 2f;

        for (int i = 0; i < 3; i++)
        {
            float line = y + 3f + i * 5f;
            g.DrawLine(new Point(x + 1f, line), new Point(x + size - 1f, line), TextColor, 1.5f);
        }

        DrawFocusRing(g, back, new CornerRadius(4f), _view.SelectionIndicatorColor);
    }

    protected override void OnClick(MouseClickEventArgs e) => _view.TogglePane();

    protected internal override string? GetToolTip(Point location) => Localization.Get(ZfText.OpenNavigation);

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(new Size(_view.CompactPaneLength, _view.ItemHeight), availableSize);
}