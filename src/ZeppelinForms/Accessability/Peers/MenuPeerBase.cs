using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A menu bar or a menu: the items are drawn by the control, so the
/// peer makes one item peer per entry and keeps it for as long as the entry is
/// in the list.</summary>
public abstract class MenuPeerBase : UIElementPeer
{
    private readonly Dictionary<MenuItem, MenuItemPeer> _items = [];

    protected MenuPeerBase(Forms.Controls.Base.UIElement owner) : base(owner) { }

    internal abstract List<MenuItem> Items { get; }

    public override IReadOnlyList<AccessibilityPeer> Children
    {
        get
        {
            // entries removed from the list take their peers with them
            foreach (MenuItem gone in _items.Keys.Where(item => !Items.Contains(item)).ToList())
                _items.Remove(gone);

            return [.. Items.Select(item => _items.TryGetValue(item, out MenuItemPeer? peer)
                ? peer
                : _items[item] = new MenuItemPeer(item, this))];
        }
    }

    internal abstract Rectangle ItemBounds(int index);

    internal abstract AccessibilityStates ItemStates(int index);

    internal abstract AccessibilityActions ItemActions(int index);

    internal abstract bool Invoke(int index);

    internal virtual bool Expand(int index) => false;

    internal virtual bool Collapse(int index) => false;
}

/// <summary>The bar across the top of a window: its items open submenus.</summary>
public class MenuBarPeer(MenuBar owner) : MenuPeerBase(owner)
{
    protected override AccessibilityRole DefaultRole => AccessibilityRole.MenuBar;

    internal override List<MenuItem> Items => owner.Items;

    internal override Rectangle ItemBounds(int index) => owner.ItemBounds(index);

    internal override AccessibilityStates ItemStates(int index) =>
        owner.Items[index].Items.Count == 0 ? AccessibilityStates.None
        : owner.OpenIndex == index ? AccessibilityStates.Expanded
        : AccessibilityStates.Collapsed;

    internal override AccessibilityActions ItemActions(int index) =>
        owner.Items[index].Items.Count == 0 ? AccessibilityActions.None
        : owner.OpenIndex == index ? AccessibilityActions.Collapse
        : AccessibilityActions.Expand;

    internal override bool Invoke(int index) => false;

    internal override bool Expand(int index)
    {
        if (owner.OpenIndex == index || owner.Items[index].Items.Count == 0) return false;

        owner.ToggleItem(index);
        return true;
    }

    internal override bool Collapse(int index)
    {
        if (owner.OpenIndex != index) return false;

        owner.ToggleItem(index);
        return true;
    }
}

/// <summary>A drop-down or context menu: its items run their actions.</summary>
public class MenuListPeer(MenuList owner) : MenuPeerBase(owner)
{
    protected override AccessibilityRole DefaultRole => AccessibilityRole.Menu;

    internal override List<MenuItem> Items => owner.Items;

    internal override Rectangle ItemBounds(int index) => owner.ItemBounds(index);

    internal override AccessibilityStates ItemStates(int index) => AccessibilityStates.None;

    internal override AccessibilityActions ItemActions(int index) => AccessibilityActions.Invoke;

    internal override bool Invoke(int index) => owner.InvokeItem(index);
}

/// <summary>One entry of a menu bar or a menu, or a separator between entries.</summary>
public class MenuItemPeer : AccessibilityPeer
{
    private readonly MenuItem _item;
    private readonly MenuPeerBase _menu;

    internal MenuItemPeer(MenuItem item, MenuPeerBase menu)
    {
        _item = item;
        _menu = menu;
    }

    private int Index => _menu.Items.IndexOf(_item);

    public override AccessibilityRole Role =>
        _item.IsSeparator ? AccessibilityRole.Separator : AccessibilityRole.MenuItem;

    public override string Name => _item.IsSeparator ? string.Empty : _item.Text;

    public override AccessibilityStates States =>
        _item.IsSeparator ? AccessibilityStates.None
        : (_item.IsEnabled && _menu.Owner.IsEffectivelyEnabled ? 0 : AccessibilityStates.Disabled)
          | _menu.ItemStates(Index);

    public override AccessibilityActions Actions =>
        _item.IsSeparator || !_item.IsEnabled ? AccessibilityActions.None : _menu.ItemActions(Index);

    /// <summary>"2 of 5" counts the entries a user can reach: separators
    /// are neither counted nor numbered.</summary>
    public override SetPosition? Position
    {
        get
        {
            if (_item.IsSeparator) return null;

            List<MenuItem> entries = [.. _menu.Items.Where(item => !item.IsSeparator)];

            return new SetPosition(entries.IndexOf(_item) + 1, entries.Count);
        }
    }

    public override AccessibilityPeer? Parent => _menu;

    public override IReadOnlyList<AccessibilityPeer> Children => [];

    public override Rectangle Bounds => Index >= 0 ? _menu.ItemBounds(Index) : default;

    public override Form? Form => _menu.Form;

    public override bool Invoke() =>
        Index >= 0 && _item.IsEnabled && _menu.Owner.IsEffectivelyEnabled && _menu.Invoke(Index);

    public override bool Expand() => Index >= 0 && _item.IsEnabled && _menu.Expand(Index);

    public override bool Collapse() => Index >= 0 && _menu.Collapse(Index);
}