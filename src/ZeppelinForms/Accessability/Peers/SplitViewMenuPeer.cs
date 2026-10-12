using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>
/// The menu a split view builds from its items: a list whose rows are drawn by the
/// menu itself, so each is a peer of its own — the back and pane buttons, the
/// entries as list items, the headers as headings. Separators are left out: a
/// reader has nothing to say about a line.
/// </summary>
public class SplitViewMenuPeer : UIElementPeer
{
    private readonly SplitViewMenu _menu;
    private readonly Dictionary<object, SplitViewMenuRowPeer> _peers = [];

    /// <summary>The keys of the two buttons: they have no item of their own.</summary>
    private static readonly object BackKey = new();
    private static readonly object ToggleKey = new();

    internal SplitViewMenuPeer(SplitViewMenu owner) : base(owner)
    {
        _menu = owner;

        SplitView view = owner.View;

        view.Items.CollectionChanged += (_, _) => RaiseStructureChanged();
        view.FooterItems.CollectionChanged += (_, _) => RaiseStructureChanged();

        view.SelectionChanged += (_, _) =>
        {
            if (FocusedDescendant is { } current)
                AccessibilityEvents.RaiseFocusChanged(current);
        };
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.List;

    protected override bool NameFromContent => true;

    protected override string? ContentName => _menu.View.PaneTitle ?? Localization.Get(ZfText.Pane);

    public override IReadOnlyList<AccessibilityPeer> Children
    {
        get
        {
            SplitView view = _menu.View;
            var peers = new List<AccessibilityPeer>();

            if (view.IsBackButtonVisible) peers.Add(PeerFor(BackKey, SplitViewMenu.RowKind.Back, null));
            if (view.IsPaneToggleButtonVisible) peers.Add(PeerFor(ToggleKey, SplitViewMenu.RowKind.Toggle, null));

            foreach (SplitViewItem item in view.Items.Concat(view.FooterItems))
            {
                if (item.Kind == SplitViewItemKind.Separator) continue;

                peers.Add(PeerFor(item, item.Kind == SplitViewItemKind.Header ? SplitViewMenu.RowKind.Header : SplitViewMenu.RowKind.Item, item));
            }

            // the entries gone from the menu leave no peers behind
            foreach (object gone in _peers.Keys.Where(k => k is SplitViewItem item && item.Owner is null).ToList())
                _peers.Remove(gone);

            return peers;
        }
    }

    /// <summary>The row the keyboard is on, while the menu has the focus.</summary>
    public override AccessibilityPeer? FocusedDescendant
    {
        get
        {
            if (!_menu.IsFocused) return null;

            List<SplitViewMenu.MenuRow> rows = _menu.Rows;
            int cursor = _menu.KeyboardRow;

            if (cursor < 0 || cursor >= rows.Count) return null;

            SplitViewMenu.MenuRow row = rows[cursor];

            return row.Kind switch
            {
                SplitViewMenu.RowKind.Back => PeerFor(BackKey, row.Kind, null),
                SplitViewMenu.RowKind.Toggle => PeerFor(ToggleKey, row.Kind, null),
                _ when row.Item is not null => PeerFor(row.Item, row.Kind, row.Item),
                _ => null,
            };
        }
    }

    private SplitViewMenuRowPeer PeerFor(object key, SplitViewMenu.RowKind kind, SplitViewItem? item) =>
        _peers.TryGetValue(key, out SplitViewMenuRowPeer? peer)
            ? peer
            : _peers[key] = new SplitViewMenuRowPeer(_menu, this, kind, item);
}

/// <summary>One row of a split view's menu: a button, an entry or a header.</summary>
public class SplitViewMenuRowPeer : AccessibilityPeer
{
    private readonly SplitViewMenu _menu;
    private readonly SplitViewMenuPeer _parent;
    private readonly SplitViewMenu.RowKind _kind;
    private readonly SplitViewItem? _item;

    internal SplitViewMenuRowPeer(SplitViewMenu menu, SplitViewMenuPeer parent, SplitViewMenu.RowKind kind, SplitViewItem? item)
    {
        _menu = menu;
        _parent = parent;
        _kind = kind;
        _item = item;
    }

    private SplitView View => _menu.View;

    public override AccessibilityRole Role => _kind switch
    {
        SplitViewMenu.RowKind.Item => AccessibilityRole.ListItem,
        SplitViewMenu.RowKind.Header => AccessibilityRole.Heading,
        _ => AccessibilityRole.Button,
    };

    public override string Name => _kind switch
    {
        SplitViewMenu.RowKind.Back => Localization.Get(ZfText.Back),
        SplitViewMenu.RowKind.Toggle => Localization.Get(View.IsPaneOpen ? ZfText.CloseNavigation : ZfText.OpenNavigation),
        _ => _item?.Text ?? string.Empty,
    };

    /// <summary>The badge is read after the name: "Inbox, 3".</summary>
    public override string? Description => _item is null ? null
        : _item.Badge is { Length: > 0 } badge
            ? _item.ToolTip is { Length: > 0 } tip ? $"{badge}. {tip}" : badge
            : _item.ToolTip;

    public override int HeadingLevel => _kind == SplitViewMenu.RowKind.Header ? 2 : 0;

    public override AccessibilityStates States
    {
        get
        {
            AccessibilityStates states = AccessibilityStates.None;

            switch (_kind)
            {
                case SplitViewMenu.RowKind.Item:
                    states |= AccessibilityStates.Selectable;

                    if (ReferenceEquals(View.SelectedItem, _item))
                        states |= AccessibilityStates.Selected;

                    if (!_item!.IsEnabled) states |= AccessibilityStates.Disabled;
                    break;

                case SplitViewMenu.RowKind.Back when !View.CanGoBack:
                    states |= AccessibilityStates.Disabled;
                    break;

                case SplitViewMenu.RowKind.Toggle:
                    states |= View.IsPaneOpen ? AccessibilityStates.Expanded : AccessibilityStates.Collapsed;
                    break;
            }

            if (!View.IsEffectivelyEnabled) states |= AccessibilityStates.Disabled;

            if (ReferenceEquals(_parent.FocusedDescendant, this)) states |= AccessibilityStates.Focused;

            return states;
        }
    }

    public override AccessibilityActions Actions => _kind switch
    {
        SplitViewMenu.RowKind.Item when _item!.IsEnabled =>
            _item.SelectsOnInvoke ? AccessibilityActions.Select | AccessibilityActions.Invoke : AccessibilityActions.Invoke,
        SplitViewMenu.RowKind.Back when View.CanGoBack => AccessibilityActions.Invoke,
        SplitViewMenu.RowKind.Toggle => View.IsPaneOpen ? AccessibilityActions.Collapse : AccessibilityActions.Expand,
        _ => AccessibilityActions.None,
    };

    /// <summary>Among the entries of its part of the menu — the top or the footer.</summary>
    public override SetPosition? Position
    {
        get
        {
            if (_kind != SplitViewMenu.RowKind.Item || _item is null) return null;

            IList<SplitViewItem> group = View.FooterItems.Contains(_item) ? View.FooterItems : View.Items;
            List<SplitViewItem> entries = [.. group.Where(i => i.Kind == SplitViewItemKind.Item)];

            int index = entries.IndexOf(_item);
            return index < 0 ? null : new SetPosition(index + 1, entries.Count);
        }
    }

    public override AccessibilityPeer? Parent => _parent;

    public override IReadOnlyList<AccessibilityPeer> Children => [];

    public override Rectangle Bounds
    {
        get
        {
            foreach (SplitViewMenu.MenuRow row in _menu.Rows)
            {
                if (row.Kind != _kind || !ReferenceEquals(row.Item, _item)) continue;

                Point origin = _menu.GetAbsolutePosition();
                return new Rectangle(new Point(origin.X + row.Bounds.X, origin.Y + row.Bounds.Y), row.Bounds.Size);
            }

            // scrolled out of the menu
            return default;
        }
    }

    public override Form? Form => _menu.FindOwner();

    public override bool Select()
    {
        if (_kind != SplitViewMenu.RowKind.Item || _item is not { IsEnabled: true, SelectsOnInvoke: true } || !View.IsEffectivelyEnabled)
            return false;

        View.SelectedItem = _item;
        return ReferenceEquals(View.SelectedItem, _item);
    }

    public override bool Invoke()
    {
        switch (_kind)
        {
            case SplitViewMenu.RowKind.Item when _item!.IsEnabled && View.IsEffectivelyEnabled:
                View.Invoke(_item);
                return true;

            case SplitViewMenu.RowKind.Back:
                return View.GoBack();

            default:
                return false;
        }
    }

    public override bool Expand()
    {
        if (_kind != SplitViewMenu.RowKind.Toggle || View.IsPaneOpen) return false;

        View.IsPaneOpen = true;
        return View.IsPaneOpen;
    }

    public override bool Collapse()
    {
        if (_kind != SplitViewMenu.RowKind.Toggle || !View.IsPaneOpen) return false;

        View.IsPaneOpen = false;
        return !View.IsPaneOpen;
    }
}

/// <summary>The pane button over the content of a split view whose closed pane hides
/// whole: a button that opens the menu.</summary>
public class SplitViewPaneButtonPeer : ButtonPeer
{
    internal SplitViewPaneButtonPeer(SplitViewPaneButton owner) : base(owner) { }

    protected override string? ContentName => Localization.Get(ZfText.OpenNavigation);
}