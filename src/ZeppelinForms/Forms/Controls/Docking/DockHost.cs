using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Docking;

/// <summary>
/// Panels the user arranges: docked side by side and as tabs, split by splitters, floated
/// into windows of their own over the layout, pinned to an edge to slide out while they are
/// used — the layout of an IDE. The arrangement is saved and restored as JSON.
/// </summary>
/// <remarks>
/// <para>A panel is a <see cref="DockItem"/>; the host puts it where it is told
/// (<see cref="DockTo"/>, <see cref="Float"/>, <see cref="AutoHide"/>) and where the user
/// drags it: a tab dragged off its strip shows the compass over the group under the
/// pointer and on the host's edges, and is docked where it is dropped — or floats, dropped
/// anywhere else. Esc cancels the drag.</para>
/// <para>The floating windows are inside the host, not windows of the system: they are the
/// same on every platform, a browser's and a phone's included.</para>
/// <para><see cref="PanelControl.Children"/> are the host's own: it makes them from the
/// layout, and puts back whatever is changed there.</para>
/// </remarks>
/// <example>
/// <code>
/// var host = new DockHost();
/// host.DockTo(new DockItem("editor", "Program.cs", editor), DockPosition.Tabbed);
/// host.DockTo(new DockItem("files", "Explorer", tree), DockPosition.Left, size: 0.25f);
/// host.DockTo(new DockItem("output", "Output", log), DockPosition.Bottom, size: 0.3f);
///
/// string layout = host.SaveLayout();
/// // ... at the next start
/// host.LoadLayout(layout);
/// </code>
/// </example>
public partial class DockHost : DecoratedPanel
{
    /// <summary>The thickness of an edge's strip of auto-hidden items.</summary>
    internal const float StripThickness = 24f;

    private readonly List<DockItem> _items = [];
    private readonly List<DockFloat> _floats = [];
    private readonly List<DockItem>[] _autoHidden = [[], [], [], []];

    private DockNode? _root;
    private DockItem? _active;

    /// <summary>The docked group last active: a tab without a target goes there.</summary>
    private DockGroup? _lastActiveGroup;

    private DockAutoHidePanel? _autoPanel;
    private DockItem? _openAutoHide;

    private DockItem? _hoveredStripItem;

    public DockHost()
    {
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        SetControlDefault(VerticalAlignmentProperty, VerticalAlignment.Stretch);
    }

    // ===== what there is =====

    /// <summary>Every item of the host, shown or hidden, in the order they came.</summary>
    public IReadOnlyList<DockItem> Items => _items;

    /// <summary>The item last used: its tab clicked, its content pressed, or shown by
    /// <see cref="Show"/>. Its group is outlined in <see cref="AccentColor"/>.</summary>
    public DockItem? ActiveItem => _active;

    public DockItem? FindItem(string id) => _items.FirstOrDefault(item => item.Id == id);

    public event EventHandler? ActiveItemChanged;

    /// <summary>An item is about to close, by its close button or <see cref="Close"/>.</summary>
    public event EventHandler<DockItemClosingEventArgs>? ItemClosing;

    /// <summary>An item closed: it is hidden (<see cref="DockItem.HideOnClose"/>) or
    /// gone from <see cref="Items"/>.</summary>
    public event EventHandler<DockItem>? ItemClosed;

    /// <summary>The arrangement changed — an item moved, a splitter or a window was
    /// dragged: the moment to save it, if it is saved as it goes.</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>The width of the splitters between docked groups.</summary>
    public float SplitterThickness
    {
        get;
        set
        {
            if (field == value) return;

            field = Math.Max(1f, value);
            Invalidate();
        }
    } = 4f;

    // ===== colors =====

    [Styled(Category = "Docking")]
    public partial Color SplitterColor { get; set; }
    private static Color SplitterColorDefault => new(255, 230, 230, 230);

    [Styled(Category = "Docking")]
    public partial Color SplitterHoverColor { get; set; }
    private static Color SplitterHoverColorDefault => new(255, 190, 190, 190);

    /// <summary>The line round a group's content and a window.</summary>
    [Styled(Category = "Docking")]
    public partial Color PanelBorderColor { get; set; }
    private static Color PanelBorderColorDefault => new(255, 214, 214, 214);

    /// <summary>The background of the floating windows and the auto-hidden panels.</summary>
    [Styled(Category = "Docking")]
    public partial Color PanelBackgroundColor { get; set; }
    private static Color PanelBackgroundColorDefault => Colors.White;

    /// <summary>The active group's outline, the compass, the open auto-hidden item's mark.</summary>
    [Styled(Category = "Docking")]
    public partial Color AccentColor { get; set; }
    private static Color AccentColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    /// <summary>A window's title bar, the strips of the auto-hidden items.</summary>
    [Styled(Category = "Docking")]
    public partial Color HeaderColor { get; set; }
    private static Color HeaderColorDefault => new(255, 240, 240, 240);

    /// <summary>The title bar of the active window and of a slid-out panel.</summary>
    [Styled(Category = "Docking")]
    public partial Color ActiveHeaderColor { get; set; }
    private static Color ActiveHeaderColorDefault => new(255, 222, 234, 252);

    /// <summary>The square under a hovered header button and strip item.</summary>
    [Styled(Category = "Docking")]
    public partial Color ButtonHoverColor { get; set; }
    private static Color ButtonHoverColorDefault => new(36, 0, 0, 0);

    /// <summary>Where a dragged item will be: the fill of the drop preview.</summary>
    [Styled(Category = "Docking")]
    public partial Color PreviewColor { get; set; }
    private static Color PreviewColorDefault => new(56, 0x0D, 0x6E, 0xFD);

    /// <summary>The background of the compass' buttons.</summary>
    [Styled(Category = "Docking")]
    public partial Color CompassColor { get; set; }
    private static Color CompassColorDefault => new(240, 255, 255, 255);

    // ===== placing items =====

    /// <summary>Dock an item: <paramref name="position"/> against <paramref name="target"/> —
    /// a tab of its group, or beside it taking <paramref name="size"/> of its extent.
    /// Without a target, against the whole layout: a tab of the group last active, or a
    /// side of everything.</summary>
    /// <param name="size">The share of the target's extent the item takes on a side:
    /// 0.25 — a quarter. A tab takes no share.</param>
    /// <remarks>An item not in the host yet is added to it; one in the host is moved.</remarks>
    public void DockTo(DockItem item, DockPosition position = DockPosition.Tabbed, DockItem? target = null, float size = 0.25f)
    {
        ArgumentNullException.ThrowIfNull(item);

        DockGroup? group = null;

        if (target is not null)
        {
            if (ReferenceEquals(target, item))
                throw new ArgumentException("An item can't be docked against itself.", nameof(target));

            if (target.Host != this || target.Group is null)
                throw new ArgumentException("The target is to be docked or floating in this host.", nameof(target));

            group = target.Group;
        }

        Adopt(item);
        Detach(item);
        Insert(item, position, group, size);

        SetActive(item);
        Changed();
    }

    /// <summary>Take an item out into a floating window, at <paramref name="bounds"/> in
    /// the host's coordinates — or where it floated last, or in the middle.</summary>
    public void Float(DockItem item, Rectangle? bounds = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        Adopt(item);

        Rectangle place = ClampWindow(bounds ?? item.LastFloatBounds ?? DefaultFloatBounds(item));

        if (item.State == DockState.Floating && item.Group is { Float: { } window, Items.Count: 1 })
        {
            window.Bounds = place;
            BringToFront(window);
        }
        else
        {
            Detach(item);
            PlaceFloating(item, place);
        }

        SetActive(item);
        Changed();
    }

    /// <summary>Pin an item to an edge: a button on the edge shows it, sliding out over the
    /// layout. Without a side — the edge nearest to where it is docked.</summary>
    public void AutoHide(DockItem item, DockSide? side = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        Adopt(item);

        DockSide edge = side ?? (item is { State: DockState.Docked, Group: { } group } ? SideOf(group, item.Side) : item.Side);

        Detach(item);
        PlaceAutoHidden(item, edge);

        FixActive();
        Changed();
    }

    /// <summary>Show an item: a hidden one goes back where it was — docked, floating or on
    /// its edge —, an auto-hidden one slides out, a tab is selected; and it is active.
    /// An item not in the host yet is added as a tab of the group last active.</summary>
    public void Show(DockItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Adopt(item);

        switch (item.State)
        {
            case DockState.Hidden:
                Restore(item);
                break;

            case DockState.AutoHidden:
                OpenAutoHide(item);
                break;

            case DockState.Docked or DockState.Floating when item.Group is { } group:
                group.Selected = item;

                if (group.Float is { } window) BringToFront(window);
                break;
        }

        SetActive(item);
        Changed();
    }

    /// <summary>Hide an item, keeping it in <see cref="Items"/> and its place in mind.</summary>
    public void Hide(DockItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Host != this || item.State == DockState.Hidden) return;

        Detach(item);

        FixActive();
        Changed();
    }

    /// <summary>Close an item as its close button does: <see cref="ItemClosing"/> may keep it;
    /// then it is hidden (<see cref="DockItem.HideOnClose"/>) or taken out of the host.
    /// True — it closed.</summary>
    public bool Close(DockItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Host != this) return false;

        var args = new DockItemClosingEventArgs(item);
        ItemClosing?.Invoke(this, args);

        if (args.Cancel) return false;

        Detach(item);

        if (!item.HideOnClose)
        {
            _items.Remove(item);
            item.Host = null;
            item.LastGroup = null;

            if (item.Content is { } content && IsShownByHost(content)) Release(content);
        }

        FixActive();
        Changed();

        ItemClosed?.Invoke(this, item);
        return true;
    }

    // ===== the model =====

    /// <summary>Take an item into the host; one of another host is refused.</summary>
    private void Adopt(DockItem item)
    {
        if (item.Host == this) return;

        if (item.Host is not null)
            throw new InvalidOperationException($"The item '{item.Id}' is in another host.");

        if (FindItem(item.Id) is not null)
            throw new ArgumentException($"The host has an item '{item.Id}' already.", nameof(item));

        item.Host = this;
        item.State = DockState.Hidden;
        _items.Add(item);
    }

    /// <summary>Take an item from where it is, keeping in mind where that was. It is
    /// hidden then, until it is put somewhere.</summary>
    private void Detach(DockItem item)
    {
        switch (item.State)
        {
            case DockState.Docked or DockState.Floating when item.Group is { } group:
                if (group.Float is { } window)
                {
                    item.LastFloatBounds = window.Bounds;
                    item.LastState = DockState.Floating;
                }
                else
                {
                    item.LastGroup = group;
                    item.Side = SideOf(group, item.Side);
                    item.LastState = DockState.Docked;
                }

                int at = group.Items.IndexOf(item);
                group.Items.RemoveAt(at);

                if (ReferenceEquals(group.Selected, item))
                    group.Selected = group.Items.Count == 0 ? null : group.Items[Math.Min(at, group.Items.Count - 1)];

                if (group.Items.Count == 0) RemoveGroup(group);
                break;

            case DockState.AutoHidden:
                _autoHidden[(int)item.Side].Remove(item);
                item.LastState = DockState.AutoHidden;

                if (ReferenceEquals(_openAutoHide, item)) CloseAutoHide(sync: false);
                break;
        }

        item.Group = null;
        item.State = DockState.Hidden;
    }

    private void RemoveGroup(DockGroup group)
    {
        if (group.Float is { } window)
            _floats.Remove(window);
        else if (_root is not null)
            _root = DockTree.Remove(_root, group);

        if (ReferenceEquals(_lastActiveGroup, group)) _lastActiveGroup = null;

        Retire(group);
    }

    /// <summary>A group out of the layout lets its view go, and the view its children.</summary>
    /// <remarks>An item keeps the group it left in mind (<see cref="DockItem.LastGroup"/>),
    /// and the group kept its view: a hidden item's content stayed a child of a view nobody
    /// showed, and the view, its strip and its tabs lived as long as the item.</remarks>
    private static void Retire(DockGroup group)
    {
        if (group.View is not { } view) return;

        group.View = null;
        SetChildren(view, []);
    }

    private static void AddToGroup(DockGroup group, DockItem item, bool select = true)
    {
        group.Items.Add(item);

        if (select || group.Selected is null) group.Selected = item;

        item.Group = group;
        item.State = group.Float is null ? DockState.Docked : DockState.Floating;
    }

    private void Insert(DockItem item, DockPosition position, DockGroup? target, float share)
    {
        if (position == DockPosition.Tabbed || target?.Float is not null)
        {
            // a floating window holds one group: a side of it is a tab of it
            AddToGroup(target ?? PrimaryGroup() ?? NewRootGroup(), item);
            return;
        }

        var group = new DockGroup();
        AddToGroup(group, item);

        item.Side = SideOf(position);

        if (_root is null)
        {
            _root = group;
            return;
        }

        _root = DockTree.InsertBeside(_root, target ?? _root, group, position, share);
    }

    private DockGroup NewRootGroup()
    {
        var group = new DockGroup();

        if (_root is null) _root = group;
        else _root = DockTree.InsertBeside(_root, _root, group, DockPosition.Right, 0.5f);

        return group;
    }

    private void PlaceFloating(DockItem item, Rectangle bounds)
    {
        var group = new DockGroup();
        var window = new DockFloat(group, bounds);
        group.Float = window;

        AddToGroup(group, item);
        _floats.Add(window);
    }

    private void PlaceAutoHidden(DockItem item, DockSide side)
    {
        item.Side = side;
        item.State = DockState.AutoHidden;
        _autoHidden[(int)side].Add(item);
    }

    /// <summary>Put an item docked where it was: into its last group while that is in the
    /// layout; into the group the others of it went to; or on its side of the layout.</summary>
    private void PlaceDocked(DockItem item)
    {
        DockGroup? last = item.LastGroup;
        DockGroup? alive = Alive(last);

        if (alive is not null)
        {
            AddToGroup(alive, item);
            return;
        }

        if (last is null)
        {
            Insert(item, DockPosition.Tabbed, null, 0f);
            return;
        }

        Insert(item, PositionOf(item.Side), null, 0.25f);

        // the others of the old group join this one as they come back
        last.Successor = item.Group;
    }

    /// <summary>Back to where a hidden item was.</summary>
    private void Restore(DockItem item)
    {
        switch (item.LastState)
        {
            case DockState.Floating when item.LastFloatBounds is { } bounds:
                PlaceFloating(item, ClampWindow(bounds));
                break;

            case DockState.AutoHidden:
                PlaceAutoHidden(item, item.Side);
                break;

            default:
                PlaceDocked(item);
                break;
        }
    }

    /// <summary>The group itself while it is in the layout, or the one its items went on to.</summary>
    private DockGroup? Alive(DockGroup? group)
    {
        for (int hops = 0; group is not null && hops < 64; hops++)
        {
            if (IsInLayout(group)) return group;

            group = group.Successor;
        }

        return null;
    }

    private bool IsInLayout(DockGroup group) =>
        group.Float is null && group.Items.Count > 0 && DockTree.Contains(_root, group);

    /// <summary>Where a tab without a target goes: the group last active, or the first.</summary>
    private DockGroup? PrimaryGroup() =>
        _lastActiveGroup is { } last && IsInLayout(last) ? last : DockTree.Groups(_root).FirstOrDefault();

    private static DockSide SideOf(DockPosition position) => position switch
    {
        DockPosition.Top => DockSide.Top,
        DockPosition.Right => DockSide.Right,
        DockPosition.Bottom => DockSide.Bottom,
        _ => DockSide.Left,
    };

    private static DockPosition PositionOf(DockSide side) => side switch
    {
        DockSide.Top => DockPosition.Top,
        DockSide.Right => DockPosition.Right,
        DockSide.Bottom => DockPosition.Bottom,
        _ => DockPosition.Left,
    };

    /// <summary>The edge of the host nearest to a group, by where its middle is.</summary>
    private DockSide SideOf(DockGroup group, DockSide fallback)
    {
        if (group.View is not { } view || view.ActualSize.Width <= 0 || ActualSize.Width <= 0 || view.FindOwner() is null)
            return fallback;

        Point at = view.GetAbsolutePosition();
        Point origin = GetAbsolutePosition();

        float dx = (at.X - origin.X + view.ActualSize.Width / 2f) / ActualSize.Width - 0.5f;
        float dy = (at.Y - origin.Y + view.ActualSize.Height / 2f) / Math.Max(1f, ActualSize.Height) - 0.5f;

        // the middle of the layout itself has no side: the item keeps the one it had
        if (Math.Abs(dx) < 0.05f && Math.Abs(dy) < 0.05f) return fallback;

        return Math.Abs(dx) >= Math.Abs(dy)
            ? (dx < 0 ? DockSide.Left : DockSide.Right)
            : (dy < 0 ? DockSide.Top : DockSide.Bottom);
    }

    // ===== groups and windows, as the views ask =====

    internal DockGroup? ActiveGroup => _active?.Group;

    internal void Select(DockGroup group, DockItem item)
    {
        group.Selected = item;
        group.View?.Sync();

        SetActive(item);
    }

    internal void ActivateGroup(DockGroup group)
    {
        if (group.Float is { } window) BringToFront(window);

        if (group.Selected is { } item) SetActive(item);
    }

    /// <summary>Pin every item of a docked group to its edge: the pin of its header.</summary>
    internal void AutoHideGroup(DockGroup group)
    {
        DockSide side = SideOf(group, group.Selected?.Side ?? DockSide.Left);

        foreach (DockItem item in group.Items.ToArray())
        {
            Detach(item);
            PlaceAutoHidden(item, side);
        }

        FixActive();
        Changed();
    }

    /// <summary>Back where it was docked, from a window or an edge.</summary>
    internal void Redock(DockItem item)
    {
        if (item.Host != this) return;

        bool wasOpen = ReferenceEquals(_openAutoHide, item);

        Detach(item);
        PlaceDocked(item);

        if (wasOpen) CloseAutoHide(sync: false);

        SetActive(item);
        Changed();
    }

    internal void RedockGroup(DockGroup group)
    {
        DockItem? selected = group.Selected;

        foreach (DockItem item in group.Items.ToArray())
        {
            Detach(item);
            PlaceDocked(item);
        }

        if (selected?.Group is { } docked) docked.Selected = selected;
        if (selected is not null) SetActive(selected);

        Changed();
    }

    /// <summary>Close every item of a window, as its close button does; the ones
    /// <see cref="ItemClosing"/> keeps stay.</summary>
    internal void CloseGroup(DockGroup group)
    {
        foreach (DockItem item in group.Items.ToArray())
            Close(item);
    }

    internal void MoveWindow(DockFloat window, Rectangle bounds)
    {
        window.Bounds = ClampWindow(bounds);
        Invalidate();
    }

    private void BringToFront(DockFloat window)
    {
        int at = _floats.IndexOf(window);

        if (at < 0 || at == _floats.Count - 1) return;

        _floats.RemoveAt(at);
        _floats.Add(window);

        // the others go under it, not it over them: the window pressed keeps its place
        // in the tree, and with it the focus and the press inside it
        SyncViews();
    }

    private Rectangle DefaultFloatBounds(DockItem item)
    {
        Size size = item.Group?.View is { ActualSize: { Width: > 0 } actual }
            ? new Size(Math.Clamp(actual.Width, 220f, 560f), Math.Clamp(actual.Height, 160f, 420f))
            : new Size(320f, 240f);

        float cascade = _floats.Count * 24f;

        return new Rectangle(
            Math.Max(0, (ActualSize.Width - size.Width) / 2f + cascade),
            Math.Max(0, (ActualSize.Height - size.Height) / 3f + cascade),
            size.Width,
            size.Height);
    }

    /// <summary>A window keeps its title bar inside the host, where it can be taken by it.</summary>
    private Rectangle ClampWindow(Rectangle bounds)
    {
        float width = Math.Max(DockFloatingWindow.MinWidth, bounds.Width);
        float height = Math.Max(DockFloatingWindow.MinHeight, bounds.Height);

        if (ActualSize.Width <= 0 || ActualSize.Height <= 0) return new Rectangle(bounds.X, bounds.Y, width, height);

        const float reach = 60f;

        float x = Math.Clamp(bounds.X, reach - width, Math.Max(0, ActualSize.Width - reach));
        float y = Math.Clamp(bounds.Y, 0, Math.Max(0, ActualSize.Height - DockFloatingWindow.TitleHeight));

        return new Rectangle(x, y, width, height);
    }

    // ===== the active item =====

    private void SetActive(DockItem? item)
    {
        if (ReferenceEquals(_active, item)) return;

        _active = item;

        if (item?.Group is { Float: null } group) _lastActiveGroup = group;

        InvalidateViews();
        ActiveItemChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The active item went out of sight: the one of the group last active takes over.</summary>
    private void FixActive()
    {
        if (_active is { } item && item.Host == this
            && (item.State is DockState.Docked or DockState.Floating || ReferenceEquals(item, _openAutoHide)))
        {
            return;
        }

        SetActive(PrimaryGroup()?.Selected ?? _floats.LastOrDefault()?.Group.Selected);
    }

    private void InvalidateViews()
    {
        foreach (DockGroup group in DockTree.Groups(_root)) group.View?.InvalidateVisual();

        foreach (DockFloat window in _floats)
        {
            window.View?.InvalidateVisual();
            window.Group.View?.InvalidateVisual();
        }
    }

    // ===== an item changed =====

    internal void OnItemChanged(DockItem item, bool contentChanged)
    {
        if (item.Group?.View is { } view)
        {
            view.Sync();
            item.Group.Float?.View?.InvalidateVisual();
        }

        if (item.State == DockState.AutoHidden)
        {
            Invalidate();

            if (contentChanged && ReferenceEquals(_openAutoHide, item)) _autoPanel?.Show(item);
        }
    }

    internal void RaiseLayoutChanged() => LayoutChanged?.Invoke(this, EventArgs.Empty);

    private void Changed()
    {
        SyncViews();
        RaiseLayoutChanged();
    }

    // ===== the views =====

    /// <summary>Make the elements match the layout: a node keeps its view while it is in
    /// the layout, so a change moves the views it touches and no others.</summary>
    internal void SyncViews()
    {
        var wanted = new List<UIElement>();

        if (_root is not null) wanted.Add(ViewOf(_root));

        if (_openAutoHide is not null && _autoPanel is not null) wanted.Add(_autoPanel);

        foreach (DockFloat window in _floats)
        {
            ViewOf(window.Group);

            DockFloatingWindow view = window.View ??= new DockFloatingWindow(this, window);
            view.Sync();
            wanted.Add(view);
        }

        SetChildren(this, wanted);
        Invalidate();
    }

    private UIElement ViewOf(DockNode node)
    {
        switch (node)
        {
            case DockGroup group:
                DockGroupView groupView = group.View ??= new DockGroupView(this, group);
                groupView.Sync();
                return groupView;

            case DockSplit split:
                DockSplitView splitView = split.View ??= new DockSplitView(this, split);
                var children = new List<UIElement>(split.Children.Count);

                foreach (DockNode child in split.Children) children.Add(ViewOf(child));

                SetChildren(splitView, children);
                splitView.Invalidate();
                return splitView;

            default:
                throw new InvalidOperationException("A node of an unknown kind.");
        }
    }

    /// <summary>Make a panel's children <paramref name="wanted"/>, moving as few as it takes.
    /// An element of another panel is taken from it first.</summary>
    internal static void SetChildren(PanelControl panel, IReadOnlyList<UIElement> wanted)
    {
        if (panel.Children.Count == wanted.Count)
        {
            bool same = true;

            for (int i = 0; same && i < wanted.Count; i++)
                same = ReferenceEquals(panel.Children[i], wanted[i]);

            if (same) return;
        }

        var keep = new HashSet<UIElement>(wanted, ReferenceEqualityComparer.Instance);

        for (int i = panel.Children.Count - 1; i >= 0; i--)
            if (!keep.Contains(panel.Children[i])) panel.Children.RemoveAt(i);

        for (int i = 0; i < wanted.Count; i++)
        {
            UIElement element = wanted[i];
            int at = panel.Children.IndexOf(element);

            if (at == i) continue;

            if (at > i)
            {
                panel.Children.Move(at, i);
                continue;
            }

            Release(element);
            panel.Children.Insert(i, element);
        }
    }

    /// <summary>The element is shown in a panel of a host: a group's, or a slid-out one.</summary>
    internal static bool IsShownByHost(UIElement element) => element.Parent is DockGroupView or DockAutoHidePanel;

    /// <summary>Take an element out of the panel it is in.</summary>
    internal static void Release(UIElement element)
    {
        switch (element.Parent)
        {
            case PanelControl panel:
                panel.Children.Remove(element);
                break;

            case WrapControl wrap when ReferenceEquals(wrap.Child, element):
                wrap.Child = null;
                break;
        }
    }

    // ===== layout =====

    private bool HasStrip(DockSide side) => _autoHidden[(int)side].Count > 0;

    /// <summary>The layout's place: the host less the strips of the auto-hidden items.</summary>
    internal Rectangle InnerRect(Size size)
    {
        float left = HasStrip(DockSide.Left) ? StripThickness : 0f;
        float top = HasStrip(DockSide.Top) ? StripThickness : 0f;
        float right = HasStrip(DockSide.Right) ? StripThickness : 0f;
        float bottom = HasStrip(DockSide.Bottom) ? StripThickness : 0f;

        return new Rectangle(left, top, Math.Max(0, size.Width - left - right), Math.Max(0, size.Height - top - bottom));
    }

    internal float ClampAutoHideExtent(DockSide side, float extent)
    {
        Rectangle inner = InnerRect(ActualSize);
        float across = side is DockSide.Left or DockSide.Right ? inner.Width : inner.Height;

        return Math.Clamp(extent, Math.Min(120f, across), Math.Max(Math.Min(120f, across), across * 0.85f));
    }

    private Rectangle AutoPanelRect(Rectangle inner)
    {
        if (_openAutoHide is not { } item) return Rectangle.Empty;

        float across = item.Side is DockSide.Left or DockSide.Right ? inner.Width : inner.Height;

        if (item.AutoHideExtent <= 0) item.AutoHideExtent = Math.Max(120f, across * 0.3f);

        float extent = Math.Clamp(item.AutoHideExtent, Math.Min(120f, across), Math.Max(Math.Min(120f, across), across * 0.85f));

        return item.Side switch
        {
            DockSide.Left => new Rectangle(inner.X, inner.Y, extent, inner.Height),
            DockSide.Right => new Rectangle(inner.Right - extent, inner.Y, extent, inner.Height),
            DockSide.Top => new Rectangle(inner.X, inner.Y, inner.Width, extent),
            _ => new Rectangle(inner.X, inner.Bottom - extent, inner.Width, extent),
        };
    }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var size = new Size(
            float.IsFinite(availableSize.Width) ? availableSize.Width : 640f,
            float.IsFinite(availableSize.Height) ? availableSize.Height : 420f);

        Rectangle inner = InnerRect(size);

        _root?.Element?.Measure(inner.Size);
        _autoPanel?.Measure(AutoPanelRect(inner).Size);

        foreach (DockFloat window in _floats)
            window.View?.Measure(window.Bounds.Size);

        return ResolveSize(size, availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        Rectangle inner = InnerRect(contentSize);

        _root?.Element?.Arrange(inner, fill: true);

        if (_openAutoHide is not null) _autoPanel?.Arrange(AutoPanelRect(inner), fill: true);

        foreach (DockFloat window in _floats)
        {
            if (window.View is not { } view) continue;

            // a host made smaller keeps its windows within reach
            Rectangle bounds = ClampWindow(window.Bounds);
            view.Arrange(bounds, fill: true);
        }
    }

    // ===== keyboard =====

    /// <summary>Esc slides an open panel back, the focus being in the host.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _openAutoHide is null || _drag is not null) return;

        CloseAutoHide(sync: true);
        e.Handled = true;
    }

    // ===== life =====

    protected override void OnDetached()
    {
        // the pointer is gone with the form: whatever was dragged stays where it was
        if (_drag is { Window: { } window } drag) window.Bounds = drag.WindowStart;

        _drag = null;
        StopListening();

        base.OnDetached();
    }
}
