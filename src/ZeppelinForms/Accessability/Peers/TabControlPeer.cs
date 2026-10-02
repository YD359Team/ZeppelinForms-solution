using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>Tabs: the strip of tab headers, then the selected tab's content.</summary>
/// <remarks>The headers are drawn by the control, so each is a peer of its own;
/// the content is an element, and comes after them as it is.</remarks>
public class TabControlPeer : UIElementPeer
{
    private readonly TabControl _tabs;
    private readonly Dictionary<TabItem, TabPeer> _peers = [];

    public TabControlPeer(TabControl owner) : base(owner)
    {
        _tabs = owner;

        owner.SelectionChanged += (_, _) =>
        {
            RaiseStructureChanged();

            if (FocusedDescendant is { } current)
                AccessibilityEvents.RaiseFocusChanged(current);
        };
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.TabList;

    public override IReadOnlyList<AccessibilityPeer> Children
    {
        get
        {
            foreach (TabItem gone in _peers.Keys.Where(tab => !_tabs.Tabs.Contains(tab)).ToList())
                _peers.Remove(gone);

            List<AccessibilityPeer> children = [.. _tabs.Tabs.Select(PeerFor)];
            children.AddRange(base.Children);

            return children;
        }
    }

    /// <summary>The selected tab, while the strip has the focus.</summary>
    public override AccessibilityPeer? FocusedDescendant =>
        _tabs is { IsFocused: true, SelectedTab: { } tab } ? PeerFor(tab) : null;

    private TabPeer PeerFor(TabItem tab) =>
        _peers.TryGetValue(tab, out TabPeer? peer) ? peer : _peers[tab] = new TabPeer(tab, _tabs, this);
}

/// <summary>One tab header: selecting it shows its content.</summary>
public class TabPeer : AccessibilityPeer
{
    private readonly TabItem _tab;
    private readonly TabControl _tabs;
    private readonly TabControlPeer _parent;

    internal TabPeer(TabItem tab, TabControl tabs, TabControlPeer parent)
    {
        _tab = tab;
        _tabs = tabs;
        _parent = parent;
    }

    private int Index => _tabs.Tabs.IndexOf(_tab);

    public override AccessibilityRole Role => AccessibilityRole.Tab;

    public override string Name => _tab.Header;

    public override AccessibilityStates States
    {
        get
        {
            AccessibilityStates states = AccessibilityStates.Selectable;

            if (ReferenceEquals(_tabs.SelectedTab, _tab))
            {
                states |= AccessibilityStates.Selected;
                if (_tabs.IsFocused) states |= AccessibilityStates.Focused;
            }

            if (!_tab.IsEnabled || !_tabs.IsEffectivelyEnabled)
                states |= AccessibilityStates.Disabled;

            return states;
        }
    }

    public override AccessibilityActions Actions =>
        _tab.IsEnabled ? AccessibilityActions.Select : AccessibilityActions.None;

    public override SetPosition? Position => new(Index + 1, _tabs.Tabs.Count);

    public override AccessibilityPeer? Parent => _parent;

    public override IReadOnlyList<AccessibilityPeer> Children => [];

    public override Rectangle Bounds
    {
        get
        {
            if (Index < 0) return default;

            Rectangle local = _tabs.HeaderRect(Index);
            Point origin = _tabs.GetAbsolutePosition();

            return new Rectangle(new Point(origin.X + local.X, origin.Y + local.Y), local.Size);
        }
    }

    public override Form? Form => _tabs.FindOwner();

    public override bool Select()
    {
        if (!_tab.IsEnabled || !_tabs.IsEffectivelyEnabled || Index < 0) return false;

        _tabs.SelectedIndex = Index;
        return true;
    }
}