using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>The window's horizontal menu bar. Opens submenus through
/// the same overlay layer as the context menu.</summary>
public partial class MenuBar : DecoratedControl
{
    private const float ItemPadding = 12f;

    private int _hoveredIndex = -1;
    private int _openIndex = -1;

    /// <summary>The bar is worked from the keyboard — F10, Alt, an access key.
    /// The highlight is then the keyboard's, and the mouse leaving the bar
    /// must not take it away.</summary>
    private bool _keyboardMode;

    /// <summary>The form whose FlyoutClosed we listen to while a submenu is open.</summary>
    private Form? _menuOwner;

    public List<MenuItem> Items { get; init; } = [];

    [Styled(Category = "Menu")]
    public partial Color HoverColor { get; set; }

    private static Color HoverColorDefault => new(255, 232, 240, 254);

    [Styled(Category = "Menu")]
    public partial Color OpenColor { get; set; }

    private static Color OpenColorDefault => new(255, 214, 228, 252);

    public MenuBar()
    {
        SetControlDefault(BackgroundProperty, new Color(255, 248, 248, 248));
        SetControlDefault(HorizontalAlignmentProperty, Enums.HorizontalAlignment.Stretch);
        SetControlDefault(VerticalAlignmentProperty, Enums.VerticalAlignment.Top);
    }

    /// <summary>The width of an item's cell, by its caption as shown —
    /// without the access key mark.</summary>
    private float WidthOf(MenuItem item) =>
        TextMeasurer.Current.MeasureText(Mnemonic.Strip(item.Text), EffectiveFont).Width + ItemPadding * 2;

    // the background, border and corner radius are drawn by the base — only the menu items here
    protected override void DrawContent(Graphics g)
    {
        float x = 0;

        for (int i = 0; i < Items.Count; i++)
        {
            float width = WidthOf(Items[i]);
            var cell = new Rectangle(new Point(x, 0), new Size(width, ActualSize.Height));

            if (i == _openIndex)
                g.FillRectangle(cell, OpenColor);
            else if (i == _hoveredIndex)
                g.FillRectangle(cell, HoverColor);

            (string caption, int accessKey) = Mnemonic.Parse(Items[i].Text);

            g.DrawText(caption, cell, TextColor, EffectiveFont,
                HorizontalContentAlignment.Center, VerticalContentAlignment.Center);

            if (FindOwner() is { ShowsAccessKeys: true })
            {
                Mnemonic.DrawUnderline(g, caption, accessKey, cell, TextColor, EffectiveFont,
                    HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
            }

            x += width;
        }
    }

    private int IndexFromPoint(Point location)
    {
        float localX = location.X - GetAbsolutePosition().X;

        float x = 0;
        for (int i = 0; i < Items.Count; i++)
        {
            float width = WidthOf(Items[i]);
            if (localX >= x && localX < x + width)
                return i;

            x += width;
        }

        return -1;
    }

    protected override void OnMouseMove(MouseMoveEventArgs args)
    {
        int index = IndexFromPoint(args.Location);
        if (index == _hoveredIndex) return;

        _hoveredIndex = index;

        // the mouse is moved along the bar with a menu already open —
        // switch on the fly, as system menus do
        if (_openIndex >= 0 && index >= 0 && index != _openIndex)
            OpenSubmenu(index);

        // hover changes only the highlight, not the geometry
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs args)
    {
        // the keyboard's highlight stays: the pointer only passed by
        if (_hoveredIndex < 0 || _keyboardMode) return;

        _hoveredIndex = -1;

        // without a redraw the hover highlight stayed on after the mouse left
        InvalidateVisual();
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        int index = IndexFromPoint(e.Location);
        if (index < 0) return;

        e.Handled = true;

        ToggleItem(index);
    }

    /// <summary>Open the item's submenu, or close it if it is the open one.
    /// The click and the accessibility peer's Expand and Collapse come here.</summary>
    internal void ToggleItem(int index)
    {
        if (_openIndex == index)
        {
            FindOwner()?.CloseAllFlyouts();
            _openIndex = -1;
            InvalidateVisual();
            return;
        }

        OpenSubmenu(index);
    }

    /// <summary>The item whose submenu is open; −1 — none.</summary>
    internal int OpenIndex => _openIndex;

    // ===== keyboard =====
    //
    // The form drives these: it knows which keys reach a menu and when menu mode
    // starts and ends. The bar only moves its highlight and opens submenus.

    /// <summary>Start being worked from the keyboard, with the first item highlighted
    /// — or the given one, for an access key.</summary>
    internal void BeginKeyboard(int index = 0)
    {
        _keyboardMode = true;
        _hoveredIndex = Items.Count == 0 ? -1 : Math.Clamp(index, 0, Items.Count - 1);
        InvalidateVisual();
    }

    internal void EndKeyboard()
    {
        _keyboardMode = false;
        _hoveredIndex = -1;
        InvalidateVisual();
    }

    /// <summary>Move the highlight to the neighbour, wrapping around. With a
    /// submenu open, the neighbour's opens in its place — as Left and Right do
    /// in every menu bar.</summary>
    internal void MoveHighlight(int step)
    {
        if (Items.Count == 0) return;

        bool reopen = _openIndex >= 0;
        _hoveredIndex = ((_hoveredIndex < 0 ? 0 : _hoveredIndex) + step + Items.Count) % Items.Count;

        if (reopen)
        {
            if (Items[_hoveredIndex].Items.Count > 0)
                OpenSubmenu(_hoveredIndex);
            else
                FindOwner()?.CloseAllFlyouts();
        }

        InvalidateVisual();
    }

    /// <summary>Open the highlighted item's submenu. False — nothing to open.</summary>
    internal bool OpenHighlighted()
    {
        if (_hoveredIndex < 0 || !Items[_hoveredIndex].IsEnabled) return false;

        OpenSubmenu(_hoveredIndex);
        return _openIndex == _hoveredIndex;
    }

    /// <summary>The item whose caption marks this access key; −1 — none.</summary>
    internal int IndexOfAccessKey(char key) =>
        Items.FindIndex(item => item.IsEnabled && Mnemonic.Key(item.Text) == key);

    /// <summary>An item's cell in the form's coordinates — for the accessibility
    /// peer, whose items are not elements and have no bounds of their own.</summary>
    internal Rectangle ItemBounds(int index)
    {
        Point origin = GetAbsolutePosition();
        float x = 0;

        for (int i = 0; i < index; i++)
            x += WidthOf(Items[i]);

        return new Rectangle(
            new Point(origin.X + x, origin.Y),
            new Size(WidthOf(Items[index]), ActualSize.Height));
    }

    private void OpenSubmenu(int index)
    {
        Form? owner = FindOwner();
        if (owner is null || Items[index].Items.Count == 0) return;

        owner.CloseAllFlyouts();

        float x = GetAbsolutePosition().X;
        for (int i = 0; i < index; i++)
            x += WidthOf(Items[i]);

        owner.ShowContextMenu(Items[index].Items,
            new Point(x, GetAbsolutePosition().Y + ActualSize.Height));

        _openIndex = index;

        // after ShowContextMenu, not before: it closes the open flyouts itself,
        // and the notification about that must not reset the index just set
        TrackClosing(owner);

        InvalidateVisual();
    }

    /// <summary>Learn about the submenu closing, however it happens: a click
    /// outside, a chosen item, the form closing all flyouts. Previously
    /// _openIndex stayed set after that: the item kept the open highlight,
    /// and hovering over the bar reopened submenus by itself.</summary>
    private void TrackClosing(Form owner)
    {
        if (ReferenceEquals(_menuOwner, owner)) return;

        StopTrackingClosing();

        _menuOwner = owner;
        owner.FlyoutClosed += OnFlyoutClosed;
    }

    private void StopTrackingClosing()
    {
        if (_menuOwner is null) return;

        _menuOwner.FlyoutClosed -= OnFlyoutClosed;
        _menuOwner = null;
    }

    private void OnFlyoutClosed(object? sender, UIElement closed)
    {
        // a bar has at most one submenu open, and switching to a neighbour
        // closes it first — so any closing means ours is gone
        StopTrackingClosing();

        if (_openIndex < 0) return;

        _openIndex = -1;
        InvalidateVisual();
    }

    protected override void OnDetached()
    {
        // the subscription must not keep the bar alive through the form
        StopTrackingClosing();

        _openIndex = -1;
        _hoveredIndex = -1;
        _keyboardMode = false;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        float width = 0;
        foreach (MenuItem item in Items)
            width += WidthOf(item);

        Size probe = TextMeasurer.Current.MeasureText("Wg", EffectiveFont);
        return ResolveSize(new Size(width, probe.Height + 10), availableSize);
    }
}