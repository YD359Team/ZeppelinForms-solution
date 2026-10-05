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

    private float HeightOf(MenuItem item) => item.IsSeparator ? SeparatorHeight : ItemHeight;

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

                if (i == _hoveredIndex && item.IsEnabled)
                    g.FillRectangle(row, HoverColor);

                Color color = !item.IsEnabled ? DisabledColor
                            : i == _hoveredIndex && HighlightedTextColor.A > 0 ? HighlightedTextColor
                            : TextColor;

                if (!string.IsNullOrEmpty(item.PathData))
                {
                    var icon = new Rectangle(
                        new Point(content.X + 4, y + (height - 14) / 2f), new Size(14, 14));

                    g.DrawSvgPath(item.PathData, icon, color);
                }

                var text = new Rectangle(
                    new Point(content.X + IconWidth, y),
                    new Size(Math.Max(0, content.Width - IconWidth - 6), height));

                (string caption, int accessKey) = Mnemonic.Parse(item.Text);

                g.DrawText(caption, text, color, EffectiveFont,
                    HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

                if (FindOwner() is { ShowsAccessKeys: true })
                {
                    Mnemonic.DrawUnderline(g, caption, accessKey, text, color, EffectiveFont,
                        HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
                }
            }

            y += height;
        }
    }

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

    protected override void OnMouseMove(MouseMoveEventArgs args)
    {
        int index = IndexFromPoint(args.Location);
        if (index == _hoveredIndex) return;

        _hoveredIndex = index;

        // hover changes only the highlight, not the geometry
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs args)
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

    /// <summary>Run the item: its Click, then ItemInvoked. The click and the
    /// accessibility peer's Invoke come here. False — a separator or a disabled item.</summary>
    internal bool InvokeItem(int index)
    {
        MenuItem item = Items[index];

        if (item.IsSeparator || !item.IsEnabled)
            return false;

        item.RaiseClick();
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

            if (!Items[index].IsSeparator && Items[index].IsEnabled)
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
            !item.IsSeparator && item.IsEnabled && Mnemonic.Key(item.Text) == key);

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

        foreach (MenuItem item in Items)
        {
            height += HeightOf(item);

            if (!item.IsSeparator)
                width = Math.Max(width, TextMeasurer.Current.MeasureText(Mnemonic.Strip(item.Text), EffectiveFont).Width);
        }

        var content = new Size(
            width + IconWidth + 20 + Padding.Horizontal,
            height + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }
}