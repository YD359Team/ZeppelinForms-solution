using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>What a <see cref="SplitViewItem"/> is in the pane's menu.</summary>
public enum SplitViewItemKind : byte
{
    /// <summary>An entry: an icon and a text that can be chosen.</summary>
    Item,

    /// <summary>A caption over a group of entries. Not chosen; while the pane is
    /// closed to its strip it shows as a line.</summary>
    Header,

    /// <summary>A line between groups.</summary>
    Separator,
}

/// <summary>
/// An entry of a <see cref="SplitView"/>'s navigation menu: an icon, a text, and
/// what the content area shows when the entry is chosen.
/// </summary>
/// <remarks>
/// <para>
/// The content is given ready, as <see cref="Content"/>, or made on demand, as
/// <see cref="ContentFactory"/>: the factory runs when the entry is chosen for the
/// first time, and what it made is kept — coming back to an entry finds its page as
/// it was left. An entry with neither leaves the content area to the application,
/// which hears <see cref="SplitView.SelectionChanged"/>.
/// </para>
/// <para>
/// <see cref="Header(string)"/> and <see cref="Separator"/> make the other kinds.
/// </para>
/// </remarks>
public sealed class SplitViewItem
{
    internal SplitView? Owner { get; set; }

    /// <summary>The content the factory made, kept for the next visit.</summary>
    internal UIElement? MadeContent { get; set; }

    public SplitViewItem() { }

    public SplitViewItem(string text, IconSource? icon = null, Func<UIElement>? content = null)
    {
        Text = text;
        Icon = icon;
        ContentFactory = content;
    }

    /// <summary>A caption over the entries after it.</summary>
    public static SplitViewItem Header(string text) => new() { Kind = SplitViewItemKind.Header, Text = text };

    /// <summary>A line between groups of entries.</summary>
    public static SplitViewItem Separator() => new() { Kind = SplitViewItemKind.Separator };

    public SplitViewItemKind Kind { get; private init; } = SplitViewItemKind.Item;

    public string Text
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Owner?.OnItemChanged();
        }
    } = string.Empty;

    /// <summary>The icon in the column the compact pane keeps while closed.</summary>
    public IconSource? Icon
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Owner?.OnItemChanged();
        }
    }

    /// <summary>The content shown when the entry is chosen, ready-made.</summary>
    public UIElement? Content { get; set; }

    /// <summary>Makes the content when the entry is chosen for the first time; what
    /// it made is kept. <see cref="Content"/>, when set, is used instead.</summary>
    public Func<UIElement>? ContentFactory { get; set; }

    /// <summary>A short mark at the end of the entry — a count of new messages, "New".
    /// While the pane is closed to its strip it shows as a dot on the icon.</summary>
    public string? Badge
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Owner?.OnItemChanged();
        }
    }

    public bool IsEnabled
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Owner?.OnItemChanged();
        }
    } = true;

    /// <summary>Whether choosing the entry selects it. False — an action, such as
    /// "Sign out": <see cref="SplitView.ItemInvoked"/> is raised, the selection and
    /// the content stay.</summary>
    public bool SelectsOnInvoke { get; set; } = true;

    /// <summary>The tooltip. Without one, an entry of a closed pane, whose text is
    /// hidden, shows its text.</summary>
    public string? ToolTip { get; set; }

    /// <summary>Whatever the application ties to the entry.</summary>
    public object? Tag { get; set; }

    internal bool IsChoosable => Kind == SplitViewItemKind.Item && IsEnabled;

    /// <summary>The content to show for the entry; the factory runs once.</summary>
    internal UIElement? ResolveContent() => Content ?? (MadeContent ??= ContentFactory?.Invoke());
}

/// <summary>The selection of a <see cref="SplitView"/>'s menu changed.</summary>
public sealed class SplitViewSelectionChangedEventArgs(SplitViewItem? oldItem, SplitViewItem? newItem) : EventArgs
{
    public SplitViewItem? OldItem { get; } = oldItem;

    public SplitViewItem? NewItem { get; } = newItem;
}

/// <summary>An entry of a <see cref="SplitView"/>'s menu was chosen — clicked or
/// activated from the keyboard — whether or not that changed the selection.</summary>
public sealed class SplitViewItemInvokedEventArgs(SplitViewItem item) : EventArgs
{
    public SplitViewItem Item { get; } = item;
}

/// <remarks>
/// <para>
/// <b>Navigation.</b> Entries added to <see cref="Items"/> and <see cref="FooterItems"/>
/// turn the pane into a menu the split view draws itself — a button that opens and
/// closes the pane, an optional back button, the entries with their icons in the
/// column the compact pane keeps, the footer entries at the bottom — and choosing an
/// entry shows its content in the content area:
/// </para>
/// <code>
/// var nav = new SplitView { DisplayMode = SplitViewDisplayMode.CompactInline };
/// nav.Items.Add(new SplitViewItem("Processes", processIcon, () =&gt; new ProcessesView()));
/// nav.Items.Add(new SplitViewItem("Charts", chartIcon, () =&gt; new ChartsView()));
/// nav.FooterItems.Add(new SplitViewItem("Settings", gearIcon, () =&gt; new SettingsView()));
/// </code>
/// <para>
/// The first entry that can be selected is selected as it is added, as the first page
/// of a <see cref="Navigation.PageControl"/> is. A menu and a <see cref="Pane"/> of one's own
/// don't mix: setting one while the other is there throws.
/// </para>
/// </remarks>
public partial class SplitView
{
    private SplitViewMenu? _menu;
    private SplitViewItem? _selectedItem;

    /// <summary>The entries chosen before, for <see cref="GoBack"/>: the latest last.</summary>
    private readonly List<SplitViewItem> _history = [];

    /// <summary>The selection was made, or cleared, by the application or the user:
    /// adding more entries no longer picks one by itself.</summary>
    private bool _selectionTouched;

    /// <summary>The menu's entries, from the top.</summary>
    public ObservableCollection<SplitViewItem> Items { get; } = [];

    /// <summary>The entries kept at the bottom of the pane — settings, the account.</summary>
    public ObservableCollection<SplitViewItem> FooterItems { get; } = [];

    /// <summary>Whether the pane is a menu of <see cref="Items"/> and <see cref="FooterItems"/>.</summary>
    public bool HasMenu => _menu is not null;

    internal SplitViewMenu? Menu => _menu;

    private void InitializeNavigation()
    {
        Items.CollectionChanged += OnItemsChanged;
        FooterItems.CollectionChanged += OnItemsChanged;
    }

    // ===== selection =====

    /// <summary>The chosen entry, of <see cref="Items"/> or <see cref="FooterItems"/>.
    /// Setting it shows its content, as choosing it does, but raises no
    /// <see cref="ItemInvoked"/>. An entry not in the menu, a header or a separator
    /// selects nothing.</summary>
    public SplitViewItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            _selectionTouched = true;

            Select(value is { Kind: SplitViewItemKind.Item } && Contains(value) ? value : null, remember: true);
        }
    }

    /// <summary>The selection changed: by the user, from code or by <see cref="GoBack"/>.
    /// The content of the new entry is already shown.</summary>
    public event EventHandler<SplitViewSelectionChangedEventArgs>? SelectionChanged;

    /// <summary>An entry was chosen by the user — also the one already selected, and
    /// one that doesn't select (<see cref="SplitViewItem.SelectsOnInvoke"/>). Raised
    /// before the selection changes.</summary>
    public event EventHandler<SplitViewItemInvokedEventArgs>? ItemInvoked;

    /// <summary>Whether <see cref="GoBack"/> has somewhere to go.</summary>
    public bool CanGoBack => _history.Count > 0;

    /// <summary>Select the entry chosen before the current one. False — there is none.</summary>
    public bool GoBack()
    {
        if (_history.Count == 0) return false;

        SplitViewItem previous = _history[^1];
        _history.RemoveAt(_history.Count - 1);

        _selectionTouched = true;
        Select(previous, remember: false);
        return true;
    }

    private bool Contains(SplitViewItem item) => Items.Contains(item) || FooterItems.Contains(item);

    private void Select(SplitViewItem? item, bool remember)
    {
        SplitViewItem? old = _selectedItem;

        if (ReferenceEquals(old, item)) return;

        if (remember && old is not null)
            _history.Add(old);

        _selectedItem = item;

        // an entry with no content of its own leaves the content to the application
        if (item?.ResolveContent() is { } content)
            Content = content;
        else if (item is null && old is not null && ReferenceEquals(Content, old.Content ?? old.MadeContent))
            Content = null;

        _menu?.OnSelectionChanged();

        SelectionChanged?.Invoke(this, new SplitViewSelectionChangedEventArgs(old, item));
    }

    /// <summary>The user chose an entry in the menu: the click or the key.</summary>
    internal void Invoke(SplitViewItem item)
    {
        if (!item.IsChoosable || !IsEffectivelyEnabled) return;

        ItemInvoked?.Invoke(this, new SplitViewItemInvokedEventArgs(item));

        if (item.SelectsOnInvoke)
        {
            _selectionTouched = true;
            Select(item, remember: true);
        }

        // a pane over the content has done its job: the user wants the page
        if (IsOverlay && IsPaneOpen)
            IsPaneOpen = false;
    }

    // ===== the menu's buttons =====

    /// <summary>The button at the top of the menu that opens and closes the pane.
    /// In the modes that hide the closed pane whole — Overlay and Inline — the same
    /// button stands over the content's corner while the pane is closed.</summary>
    public bool IsPaneToggleButtonVisible
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            SyncParts();
            _menu?.Invalidate();
        }
    } = true;

    /// <summary>A back button above the menu, enabled while <see cref="CanGoBack"/>.</summary>
    public bool IsBackButtonVisible
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            _menu?.Invalidate();
        }
    }

    // ===== the menu's look =====

    /// <summary>The height of an entry of the menu.</summary>
    [Styled(Category = "Menu", AffectsLayout = true)]
    public partial float ItemHeight { get; set; }
    private static float ItemHeightDefault => 40f;

    [Styled(Category = "Menu")]
    public partial Color ItemHoverColor { get; set; }
    private static Color ItemHoverColorDefault => new(255, 234, 234, 234);

    [Styled(Category = "Menu")]
    public partial Color ItemPressedColor { get; set; }
    private static Color ItemPressedColorDefault => new(255, 224, 224, 224);

    [Styled(Category = "Menu")]
    public partial Color SelectedItemColor { get; set; }
    private static Color SelectedItemColorDefault => new(255, 234, 234, 234);

    /// <summary>The mark beside the selected entry.</summary>
    [Styled(Category = "Menu")]
    public partial Color SelectionIndicatorColor { get; set; }
    private static Color SelectionIndicatorColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    [Styled(Category = "Menu")]
    public partial Color HeaderTextColor { get; set; }
    private static Color HeaderTextColorDefault => new(255, 96, 96, 96);

    [Styled(Category = "Menu")]
    public partial Color SeparatorColor { get; set; }
    private static Color SeparatorColorDefault => new(255, 224, 224, 224);

    [Styled(Category = "States")]
    public partial Color DisabledTextColor { get; set; }
    private static Color DisabledTextColorDefault => new(255, 165, 165, 165);

    [Styled(Category = "Menu")]
    public partial Color BadgeColor { get; set; }
    private static Color BadgeColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    [Styled(Category = "Menu")]
    public partial Color BadgeTextColor { get; set; }
    private static Color BadgeTextColorDefault => Colors.White;

    // ===== the entries =====

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (SplitViewItem item in e.OldItems)
                if (!Contains(item)) Forget(item);

        if (e.NewItems is not null)
            foreach (SplitViewItem item in e.NewItems)
                item.Owner = this;

        // Clear() reports no old items: whatever is gone is found by what is left
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (SplitViewItem gone in _history.Where(i => !Contains(i)).ToList())
                Forget(gone);

            if (_selectedItem is not null && !Contains(_selectedItem))
                Forget(_selectedItem);
        }

        bool any = Items.Count + FooterItems.Count > 0;

        if (any && _menu is null)
        {
            // a pane of one's own and a menu would both want the pane
            if (Pane is not null)
                throw new InvalidOperationException(
                    "The SplitView already has a Pane of its own; its Items and FooterItems build the pane themselves. Use one or the other.");

            _menu = new SplitViewMenu(this);
            SetPaneCore(_menu);
        }
        else if (!any && _menu is not null)
        {
            _menu = null;
            SetPaneCore(null);
        }

        // the first entry that can be selected is, until the selection was made by hand
        if (_selectedItem is null && !_selectionTouched &&
            Items.Concat(FooterItems).FirstOrDefault(i => i.IsChoosable && i.SelectsOnInvoke) is { } first)
            Select(first, remember: false);

        _menu?.OnItemsChanged();
    }

    /// <summary>An entry left the menu: it is no longer selected, nor somewhere to go back to.</summary>
    private void Forget(SplitViewItem item)
    {
        item.Owner = null;
        _history.RemoveAll(i => ReferenceEquals(i, item));

        // the selection doesn't jump to another entry by itself: nothing is selected,
        // as in WinUI, until the application or the user picks one
        if (ReferenceEquals(_selectedItem, item))
        {
            _selectionTouched = true;
            Select(null, remember: false);
        }
    }

    internal void OnItemChanged() => _menu?.Invalidate();
}