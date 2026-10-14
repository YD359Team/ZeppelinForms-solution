using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>A vertical list of menu items. Draws everything itself,
/// without nested controls — that is simpler with hover and separators.</summary>
public partial class MenuList : DecoratedControl
{
    private const float ItemHeight = 26f;
    private const float SeparatorHeight = 7f;
    private const float IconWidth = 22f;
    /// <summary>The room between the caption and the shortcut right of it.</summary>
    private const float GestureGap = 24f;
    private int _hoveredIndex = -1;

    public List<MenuItem> Items { get; init; } = [];

    public event EventHandler<MenuItem>? ItemInvoked;

    [Styled(Category = "Menu")]
    public partial Color DisabledColor { get; set; }
    private static Color DisabledColorDefault => new(255, 160, 160, 160);

    [Styled(Category = "Menu")]
    public partial Color HoverColor { get; set; }
    private static Color HoverColorDefault => new(255, 232, 240, 254);

    /// <summary>The text of the highlighted item. Transparent — the ordinary text
    /// color; a contrast theme pairs its own with the highlight fill.</summary>
    [Styled(Category = "Menu")]
    public partial Color HighlightedTextColor { get; set; }
    private static Color HighlightedTextColorDefault => Colors.Transparent;

    [Styled(Category = "Menu")]
    public partial Color SeparatorColor { get; set; }
    private static Color SeparatorColorDefault => new(255, 220, 220, 220);

    public MenuList()
    {
        SetControlDefault(BackgroundProperty, Colors.White);
        SetControlDefault(PaddingProperty, new(2, 4));
    }

    private static float HeightOf(MenuItem item) => item.IsSeparator ? SeparatorHeight : ItemHeight;

    // the background, border and corner radius are drawn by the base — only the menu items here
    // the background, border and corner radius are drawn by the base — only the menu items here
    protected override void DrawContent(Graphics g)
    {
        var content = this.ContentBounds;
        float y = content.Y;

        for (int i = 0; i < Items.Count; i++)
        {
            MenuItem item = Items[i];
            float height = HeightOf(item);

            if (item.IsSeparator)
            {
                float lineY = y + height / 2f;
                g.DrawLine(new Point(content.X + 6, lineY),
                    new Point(content.X + content.Width - 6, lineY), SeparatorColor, 1f);
            }
            else
            {
                var row = new Rectangle(new Point(content.X, y), new Size(content.Width, height));

                // asked once per item and frame: CanExecute of a command may do work
                bool enabled = item.IsEffectivelyEnabled;

                if (i == _hoveredIndex && enabled)
                    g.FillRectangle(row, HoverColor);

                Color color = !enabled ? DisabledColor
                            : i == _hoveredIndex && HighlightedTextColor.A > 0 ? HighlightedTextColor
                            : TextColor;

                var icon = new Rectangle(
                    new Point(content.X + 4, y + (height - 14) / 2f), new Size(14, 14));

                bool hasIcon = !string.IsNullOrEmpty(item.PathData);

                if (hasIcon)
                    g.DrawSvgPath(item.PathData!, icon, color);

                // the check takes the icon's place; an item with both keeps its icon
                // and frames it, as Windows menus do
                if (item.IsEffectivelyChecked)
                {
                    if (hasIcon)
                        g.DrawRoundRectangle(Grow(icon, 2f), new CornerRadius(3f), color, 1f);
                    else
                        DrawCheckMark(g, icon, color);
                }

                var text = new Rectangle(
                    new Point(content.X + IconWidth, y),
                    new Size(Math.Max(0, content.Width - IconWidth - 6), height));

                (string caption, int accessKey) = Mnemonic.Parse(item.Text);

                g.DrawText(caption, text, color, EffectiveFont,
                    HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

                // the shortcut at the right edge, in the caption's color
                if (item.GestureText is { Length: > 0 } gesture)
                {
                    g.DrawText(gesture, text, color, EffectiveFont,
                        HorizontalContentAlignment.Right, VerticalContentAlignment.Center);
                }

                if (FindOwner() is { ShowsAccessKeys: true })
                {
                    Mnemonic.DrawUnderline(g, caption, accessKey, text, color, EffectiveFont,
                        HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
                }
            }

            y += height;
        }
    }

    private static void DrawCheckMark(Graphics g, Rectangle box, Color color)
    {
        // the same proportions as the check box's mark
        ReadOnlySpan<Point> points =
        [
            new(box.X + box.Width * 0.18f, box.Y + box.Height * 0.52f),
            new(box.X + box.Width * 0.40f, box.Y + box.Height * 0.74f),
            new(box.X + box.Width * 0.82f, box.Y + box.Height * 0.28f),
        ];

        g.DrawPolyline(points, color, 1.6f);
    }

    private static Rectangle Grow(Rectangle rect, float by) =>
        new(new Point(rect.X - by, rect.Y - by), new Size(rect.Width + by * 2, rect.Height + by * 2));

    private int IndexFromPoint(Point location)
    {
        Point abs = GetAbsolutePosition();
        float localY = location.Y - abs.Y - Padding.Top;

        float y = 0;
        for (int i = 0; i < Items.Count; i++)
        {
            float height = HeightOf(Items[i]);
            if (localY >= y && localY < y + height)
                return i;

            y += height;
        }

        return -1;
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        int index = IndexFromPoint(e.Location);
        if (index == _hoveredIndex) return;

        _hoveredIndex = index;

        // hover changes only the highlight, not the geometry
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (_hoveredIndex < 0) return;

        _hoveredIndex = -1;

        // without a redraw the hover highlight stayed on after the mouse left
        InvalidateVisual();
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        int index = IndexFromPoint(e.Location);
        if (index < 0) return;

        e.Handled = true;

        InvokeItem(index);
    }

    /// <summary>Run the item: its Click and command, then ItemInvoked. The click and the
    /// accessibility peer's Invoke come here. False — a separator or a disabled item.</summary>
    internal bool InvokeItem(int index)
    {
        MenuItem item = Items[index];

        if (!item.Invoke())
            return false;

        ItemInvoked?.Invoke(this, item);

        return true;
    }

    // ===== keyboard =====
    //
    // The highlight is shared with the mouse, as in system menus: the arrows move
    // what hover shows, and Enter runs it. The form routes the keys here while the
    // menu is the topmost flyout.

    /// <summary>The highlighted item; −1 — none.</summary>
    internal int HighlightedIndex => _hoveredIndex;

    /// <summary>Move the highlight by one, wrapping around, over the items that can
    /// be run: separators and disabled items are passed by.</summary>
    internal void MoveHighlight(int step)
    {
        if (Items.Count == 0) return;

        int index = _hoveredIndex < 0 ? (step > 0 ? -1 : Items.Count) : _hoveredIndex;

        for (int i = 0; i < Items.Count; i++)
        {
            index = (index + step + Items.Count) % Items.Count;

            if (Items[index].IsEffectivelyEnabled)
            {
                _hoveredIndex = index;
                InvalidateVisual();
                return;
            }
        }
    }

    internal void HighlightFirst()
    {
        _hoveredIndex = -1;
        MoveHighlight(1);
    }

    internal void HighlightLast()
    {
        _hoveredIndex = -1;
        MoveHighlight(-1);
    }

    /// <summary>Run the highlighted item. False — none is highlighted.</summary>
    internal bool InvokeHighlighted() => _hoveredIndex >= 0 && InvokeItem(_hoveredIndex);

    /// <summary>Run the item whose caption marks this access key.</summary>
    internal bool InvokeAccessKey(char key)
    {
        int index = Items.FindIndex(item =>
            item.IsEffectivelyEnabled && Mnemonic.Key(item.Text) == key);

        return index >= 0 && InvokeItem(index);
    }

    /// <summary>An item's row in the form's coordinates — for the accessibility
    /// peer, whose items are not elements and have no bounds of their own.</summary>
    internal Rectangle ItemBounds(int index)
    {
        Point origin = GetAbsolutePosition();
        float y = Padding.Top;

        for (int i = 0; i < index; i++)
            y += HeightOf(Items[i]);

        return new Rectangle(
            new Point(origin.X, origin.Y + y),
            new Size(ActualSize.Width, HeightOf(Items[index])));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        float height = 0;
        float width = 0;
        float gestureWidth = 0;

        foreach (MenuItem item in Items)
        {
            height += HeightOf(item);

            if (item.IsSeparator) continue;

            width = Math.Max(width, TextMeasurer.Current.MeasureText(Mnemonic.Strip(item.Text), EffectiveFont).Width);

            // the shortcuts form a column of their own: the widest caption and the
            // widest shortcut, not the widest pair, so the column lines up
            if (item.GestureText is { Length: > 0 } gesture)
                gestureWidth = Math.Max(gestureWidth, TextMeasurer.Current.MeasureText(gesture, EffectiveFont).Width);
        }

        if (gestureWidth > 0)
            width += GestureGap + gestureWidth;

        var content = new Size(
            width + IconWidth + 20 + Padding.Horizontal,
            height + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }
}