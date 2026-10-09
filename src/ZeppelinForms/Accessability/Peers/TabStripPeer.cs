using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A tab strip: a tab list whose tabs are drawn by the control, so each
/// is a peer of its own — the same shape as <see cref="TabControlPeer"/> without
/// the panel.</summary>
public class TabStripPeer : UIElementPeer
{
    private readonly TabStrip _strip;
    private readonly Dictionary<TabStripItem, TabStripTabPeer> _peers = [];

    public TabStripPeer(TabStrip owner) : base(owner)
    {
        _strip = owner;

        owner.Items.CollectionChanged += (_, _) => RaiseStructureChanged();

        owner.SelectionChanged += (_, _) =>
        {
            if (FocusedDescendant is { } current)
                AccessibilityEvents.RaiseFocusChanged(current);
        };
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.TabList;

    public override IReadOnlyList<AccessibilityPeer> Children
    {
        get
        {
            foreach (TabStripItem gone in _peers.Keys.Where(item => !_strip.Items.Contains(item)).ToList())
                _peers.Remove(gone);

            return [.. _strip.Items.Select(PeerFor)];
        }
    }

    /// <summary>The selected tab, while the strip has the focus.</summary>
    public override AccessibilityPeer? FocusedDescendant =>
        _strip is { IsFocused: true, SelectedItem: { } item } ? PeerFor(item) : null;

    private TabStripTabPeer PeerFor(TabStripItem item) =>
        _peers.TryGetValue(item, out TabStripTabPeer? peer) ? peer : _peers[item] = new TabStripTabPeer(item, _strip, this);
}

/// <summary>One tab of a strip.</summary>
public class TabStripTabPeer : AccessibilityPeer
{
    private readonly TabStripItem _item;
    private readonly TabStrip _strip;
    private readonly TabStripPeer _parent;

    internal TabStripTabPeer(TabStripItem item, TabStrip strip, TabStripPeer parent)
    {
        _item = item;
        _strip = strip;
        _parent = parent;
    }

    private int Index => _strip.Items.IndexOf(_item);

    public override AccessibilityRole Role => AccessibilityRole.Tab;

    public override string Name => _item.Header;

    public override string? Description => _item.ToolTip;

    public override AccessibilityStates States
    {
        get
        {
            AccessibilityStates states = AccessibilityStates.Selectable;

            if (ReferenceEquals(_strip.SelectedItem, _item))
            {
                states |= AccessibilityStates.Selected;
                if (_strip.IsFocused) states |= AccessibilityStates.Focused;
            }

            if (!_item.IsEnabled || !_strip.IsEffectivelyEnabled)
                states |= AccessibilityStates.Disabled;

            return states;
        }
    }

    public override AccessibilityActions Actions =>
        _item.IsEnabled ? AccessibilityActions.Select : AccessibilityActions.None;

    public override SetPosition? Position => new(Index + 1, _strip.Items.Count);

    public override AccessibilityPeer? Parent => _parent;

    public override IReadOnlyList<AccessibilityPeer> Children => [];

    public override Rectangle Bounds
    {
        get
        {
            if (Index < 0) return default;

            Rectangle local = _strip.TabRect(Index);
            Point origin = _strip.GetAbsolutePosition();

            return new Rectangle(new Point(origin.X + local.X, origin.Y + local.Y), local.Size);
        }
    }

    public override Form? Form => _strip.FindOwner();

    public override bool Select()
    {
        if (!_item.IsEnabled || !_strip.IsEffectivelyEnabled || Index < 0) return false;

        _strip.SelectedItem = _item;
        return true;
    }
}