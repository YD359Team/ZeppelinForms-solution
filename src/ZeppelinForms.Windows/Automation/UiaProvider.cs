using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Windows.Automation;

/// <summary>A UIA provider over one peer: what the peer says, in UIA's terms.</summary>
/// <remarks>
/// <para>
/// One object implements every pattern; GetPatternProvider hands it out only for
/// the patterns the peer supports at the moment, so a check box offers Toggle and a
/// button Invoke, and a combo box offers ExpandCollapse while the peer lists it.
/// </para>
/// <para>
/// A provider whose peer left its form answers UIA_E_ELEMENTNOTAVAILABLE: the
/// client holds a reference to something that is no longer on screen, and that is
/// the error UIA expects for it.
/// </para>
/// </remarks>
[GeneratedComClass]
internal partial class UiaProvider :
    IRawElementProviderSimple,
    IRawElementProviderFragment,
    IInvokeProvider,
    IToggleProvider,
    IExpandCollapseProvider,
    ISelectionItemProvider,
    IValueProvider,
    IRangeValueProvider,
    IScrollItemProvider
{
    private const int UIA_E_ELEMENTNOTAVAILABLE = unchecked((int)0x80040201);

    protected readonly UiaBridge Bridge;

    public UiaProvider(UiaBridge bridge, AccessibilityPeer peer)
    {
        Bridge = bridge;
        Peer = peer;
        RuntimeId = UiaBridge.NextRuntimeId();
    }

    public AccessibilityPeer Peer { get; }

    public int RuntimeId { get; }

    /// <summary>The peer, while it is still in this window.</summary>
    private AccessibilityPeer Live =>
        ReferenceEquals(Peer.Form, Bridge.Form)
            ? Peer
            : throw new COMException("The element is no longer available.", UIA_E_ELEMENTNOTAVAILABLE);

    // ===== IRawElementProviderSimple =====

    public int get_ProviderOptions() => UiaNative.ProviderOptions_ServerSideProvider;

    public nint GetPatternProvider(int patternId) => Bridge.OnUiThread(() =>
    {
        Guid? iid = patternId switch
        {
            UiaNative.InvokePatternId when SupportsInvoke => typeof(IInvokeProvider).GUID,
            UiaNative.TogglePatternId when SupportsToggle => typeof(IToggleProvider).GUID,
            UiaNative.ExpandCollapsePatternId when SupportsExpandCollapse => typeof(IExpandCollapseProvider).GUID,
            UiaNative.SelectionItemPatternId when SupportsSelectionItem => typeof(ISelectionItemProvider).GUID,
            UiaNative.ValuePatternId when SupportsValue => typeof(IValueProvider).GUID,
            UiaNative.RangeValuePatternId when SupportsRangeValue => typeof(IRangeValueProvider).GUID,
            UiaNative.ScrollItemPatternId when SupportsScrollItem => typeof(IScrollItemProvider).GUID,
            _ => null,
        };

        return iid is { } guid ? UiaBridge.ComPointer(this, guid) : 0;
    });

    public UiaVariant GetPropertyValue(int propertyId) => Bridge.OnUiThread(() => Property(propertyId));

    public virtual nint get_HostRawElementProvider() => 0;

    private UiaVariant Property(int propertyId)
    {
        AccessibilityPeer peer = Live;
        AccessibilityStates states = peer.States;

        return propertyId switch
        {
            UiaNative.ControlTypePropertyId => UiaVariant.From(ControlType(peer.Role)),
            UiaNative.LocalizedControlTypePropertyId when peer.Role == AccessibilityRole.Switch => UiaVariant.From("switch"),
            UiaNative.NamePropertyId => UiaVariant.From(peer.Name),
            UiaNative.HelpTextPropertyId when peer.Description is { } help => UiaVariant.From(help),
            UiaNative.AccessKeyPropertyId when peer.AccessKey is { } key => UiaVariant.From(key),
            UiaNative.AutomationIdPropertyId => UiaVariant.From(RuntimeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            UiaNative.ClassNamePropertyId => UiaVariant.From(peer.Element?.GetType().Name ?? peer.GetType().Name),
            UiaNative.FrameworkIdPropertyId => UiaVariant.From("ZeppelinForms"),
            UiaNative.ProcessIdPropertyId => UiaVariant.From(Environment.ProcessId),
            UiaNative.IsEnabledPropertyId => UiaVariant.From(!states.HasFlag(AccessibilityStates.Disabled)),
            UiaNative.HasKeyboardFocusPropertyId => UiaVariant.From(states.HasFlag(AccessibilityStates.Focused)),
            UiaNative.IsKeyboardFocusablePropertyId => UiaVariant.From(states.HasFlag(AccessibilityStates.Focusable)),
            UiaNative.IsPasswordPropertyId => UiaVariant.From(states.HasFlag(AccessibilityStates.Protected)),
            UiaNative.IsDialogPropertyId => UiaVariant.From(peer.Role == AccessibilityRole.Dialog),
            UiaNative.IsOffscreenPropertyId => UiaVariant.From(peer.Bounds.Width <= 0 || peer.Bounds.Height <= 0),

            // a layout container is in the raw view only: screen readers walk the
            // control view, and a pane around every stack panel is noise there
            UiaNative.IsControlElementPropertyId or UiaNative.IsContentElementPropertyId =>
                UiaVariant.From(peer.Role != AccessibilityRole.None),

            UiaNative.LiveSettingPropertyId => UiaVariant.From((int)peer.LiveSetting),
            UiaNative.HeadingLevelPropertyId => UiaVariant.From(UiaNative.HeadingLevelNone + Math.Clamp(peer.HeadingLevel, 0, 9)),
            UiaNative.LevelPropertyId when peer.Level > 0 => UiaVariant.From(peer.Level),
            UiaNative.PositionInSetPropertyId when peer.Position is { } position => UiaVariant.From(position.Index),
            UiaNative.SizeOfSetPropertyId when peer.Position is { } position => UiaVariant.From(position.Count),

            UiaNative.LabeledByPropertyId when peer.Element?.LabeledBy?.GetAccessibilityPeer() is { } label =>
                UiaVariant.FromUnknown(Bridge.PointerFor(label, typeof(IRawElementProviderSimple).GUID)),

            _ => UiaVariant.Empty,
        };
    }

    /// <summary>The UIA control type of a role: the nearest where UIA has none
    /// of its own — a switch is a button with the Toggle pattern.</summary>
    private static int ControlType(AccessibilityRole role) => role switch
    {
        AccessibilityRole.Window or AccessibilityRole.Dialog => UiaNative.WindowControlTypeId,
        AccessibilityRole.Group => UiaNative.GroupControlTypeId,
        AccessibilityRole.Text or AccessibilityRole.Heading => UiaNative.TextControlTypeId,
        AccessibilityRole.Link => UiaNative.HyperlinkControlTypeId,
        AccessibilityRole.Image => UiaNative.ImageControlTypeId,
        AccessibilityRole.Button or AccessibilityRole.ToggleButton or AccessibilityRole.Switch => UiaNative.ButtonControlTypeId,
        AccessibilityRole.SplitButton => UiaNative.SplitButtonControlTypeId,
        AccessibilityRole.CheckBox => UiaNative.CheckBoxControlTypeId,
        AccessibilityRole.RadioButton => UiaNative.RadioButtonControlTypeId,
        AccessibilityRole.TextBox => UiaNative.EditControlTypeId,
        AccessibilityRole.SpinButton => UiaNative.SpinnerControlTypeId,
        AccessibilityRole.Slider => UiaNative.SliderControlTypeId,
        AccessibilityRole.ProgressBar => UiaNative.ProgressBarControlTypeId,
        AccessibilityRole.ScrollBar => UiaNative.ScrollBarControlTypeId,
        AccessibilityRole.ComboBox => UiaNative.ComboBoxControlTypeId,
        AccessibilityRole.List => UiaNative.ListControlTypeId,
        AccessibilityRole.ListItem => UiaNative.ListItemControlTypeId,
        AccessibilityRole.Tree => UiaNative.TreeControlTypeId,
        AccessibilityRole.TreeItem => UiaNative.TreeItemControlTypeId,
        AccessibilityRole.Table => UiaNative.DataGridControlTypeId,
        AccessibilityRole.Row => UiaNative.DataItemControlTypeId,
        AccessibilityRole.Cell => UiaNative.TextControlTypeId,
        AccessibilityRole.ColumnHeader => UiaNative.HeaderItemControlTypeId,
        AccessibilityRole.TabList => UiaNative.TabControlTypeId,
        AccessibilityRole.Tab => UiaNative.TabItemControlTypeId,
        AccessibilityRole.MenuBar => UiaNative.MenuBarControlTypeId,
        AccessibilityRole.Menu => UiaNative.MenuControlTypeId,
        AccessibilityRole.MenuItem => UiaNative.MenuItemControlTypeId,
        AccessibilityRole.Separator => UiaNative.SeparatorControlTypeId,
        AccessibilityRole.Calendar => UiaNative.CalendarControlTypeId,
        AccessibilityRole.ToolTip => UiaNative.ToolTipControlTypeId,
        _ => UiaNative.PaneControlTypeId,
    };

    // ===== IRawElementProviderFragment =====

    public nint Navigate(int direction) => Bridge.OnUiThread(() =>
    {
        AccessibilityPeer peer = Live;
        Guid fragment = typeof(IRawElementProviderFragment).GUID;

        switch (direction)
        {
            case UiaNative.Navigate_Parent:
                return Bridge.PointerFor(peer.Parent, fragment);

            case UiaNative.Navigate_FirstChild:
                return Bridge.PointerFor(peer.Children.FirstOrDefault(), fragment);

            case UiaNative.Navigate_LastChild:
                return Bridge.PointerFor(peer.Children.LastOrDefault(), fragment);

            case UiaNative.Navigate_NextSibling or UiaNative.Navigate_PreviousSibling:
                if (peer.Parent is not { } parent) return 0;

                IReadOnlyList<AccessibilityPeer> siblings = parent.Children;
                int index = IndexOf(siblings, peer);

                if (index < 0) return 0;

                index += direction == UiaNative.Navigate_NextSibling ? 1 : -1;

                return index >= 0 && index < siblings.Count
                    ? Bridge.PointerFor(siblings[index], fragment)
                    : 0;

            default:
                return 0;
        }
    });

    private static int IndexOf(IReadOnlyList<AccessibilityPeer> peers, AccessibilityPeer peer)
    {
        for (int i = 0; i < peers.Count; i++)
        {
            if (ReferenceEquals(peers[i], peer))
                return i;
        }

        return -1;
    }

    /// <summary>{ UiaAppendRuntimeId, own number }: UIA prefixes the window's id,
    /// and the pair is unique on the whole desktop.</summary>
    public nint GetRuntimeId()
    {
        nint array = UiaNative.SafeArrayCreateVector(UiaNative.VT_I4, 0, 2);

        int index = 0;
        int value = UiaNative.UiaAppendRuntimeId;
        UiaNative.SafeArrayPutElement(array, ref index, ref value);

        index = 1;
        value = RuntimeId;
        UiaNative.SafeArrayPutElement(array, ref index, ref value);

        return array;
    }

    public virtual UiaRect get_BoundingRectangle() => Bridge.OnUiThread(() => Bridge.ToScreen(Live.Bounds));

    public nint GetEmbeddedFragmentRoots() => 0;

    public void SetFocus() => Bridge.OnUiThread(() => Live.Focus());

    public nint get_FragmentRoot() => Bridge.RootPointer(typeof(IRawElementProviderFragmentRoot).GUID);

    // ===== patterns =====

    internal bool SupportsInvoke => Peer.Actions.HasFlag(AccessibilityActions.Invoke);

    internal bool SupportsToggle => Peer.Actions.HasFlag(AccessibilityActions.Toggle);

    internal bool SupportsExpandCollapse =>
        (Peer.Actions & (AccessibilityActions.Expand | AccessibilityActions.Collapse)) != 0 ||
        (Peer.States & (AccessibilityStates.Expanded | AccessibilityStates.Collapsed)) != 0;

    internal bool SupportsSelectionItem => Peer.States.HasFlag(AccessibilityStates.Selectable);

    internal bool SupportsValue => Peer.Value is not null && Peer.Range is null;

    internal bool SupportsRangeValue => Peer.Range is not null;

    internal bool SupportsScrollItem => Peer.Actions.HasFlag(AccessibilityActions.ScrollIntoView);

    /// <summary>ToggleState: Off 0, On 1, Indeterminate 2.</summary>
    internal int CurrentToggleState =>
        Peer.States.HasFlag(AccessibilityStates.Mixed) ? 2
        : Peer.States.HasFlag(AccessibilityStates.Checked) ? 1
        : 0;

    /// <summary>ExpandCollapseState: Collapsed 0, Expanded 1, LeafNode 3.</summary>
    internal int CurrentExpandCollapseState =>
        Peer.States.HasFlag(AccessibilityStates.Expanded) ? 1
        : Peer.States.HasFlag(AccessibilityStates.Collapsed) ? 0
        : 3;

    public void Invoke() => Bridge.OnUiThread(() => Act(Live.Invoke()));

    public void Toggle() => Bridge.OnUiThread(() => Act(Live.Toggle()));

    public int get_ToggleState() => Bridge.OnUiThread(() => CurrentToggleState);

    public void Expand() => Bridge.OnUiThread(() => Act(Live.Expand()));

    public void Collapse() => Bridge.OnUiThread(() => Act(Live.Collapse()));

    public int get_ExpandCollapseState() => Bridge.OnUiThread(() => CurrentExpandCollapseState);

    public void Select() => Bridge.OnUiThread(() => Act(Live.Select()));

    /// <summary>The model selects one item at a time through its peers: adding
    /// selects, removing is not supported.</summary>
    public void AddToSelection() => Bridge.OnUiThread(() => Act(Live.Select()));

    public void RemoveFromSelection() => throw new InvalidOperationException("Removing from a selection is not supported.");

    public bool get_IsSelected() => Bridge.OnUiThread(() => Live.States.HasFlag(AccessibilityStates.Selected));

    public nint get_SelectionContainer() => Bridge.OnUiThread(() =>
        Bridge.PointerFor(Live.Parent, typeof(IRawElementProviderSimple).GUID));

    public void SetValue(string value) => Bridge.OnUiThread(() => Act(Live.SetValue(value)));

    public string get_Value() => Bridge.OnUiThread(() => Live.Value ?? string.Empty);

    public bool get_IsReadOnly() => Bridge.OnUiThread(() =>
        !Live.Actions.HasFlag(AccessibilityActions.SetValue) || Live.States.HasFlag(AccessibilityStates.ReadOnly));

    void IRangeValueProvider.SetValue(double value) => Bridge.OnUiThread(() => Act(Live.SetRangeValue(value)));

    double IRangeValueProvider.get_Value() => Bridge.OnUiThread(() => Live.Range?.Value ?? 0);

    bool IRangeValueProvider.get_IsReadOnly() => Bridge.OnUiThread(() => !Live.Actions.HasFlag(AccessibilityActions.SetValue));

    public double get_Maximum() => Bridge.OnUiThread(() => Live.Range?.Maximum ?? 0);

    public double get_Minimum() => Bridge.OnUiThread(() => Live.Range?.Minimum ?? 0);

    /// <summary>A tenth of the range: the peer knows only the small step.</summary>
    public double get_LargeChange() => Bridge.OnUiThread(() =>
        Live.Range is { } range ? Math.Max(range.SmallChange, (range.Maximum - range.Minimum) / 10) : 0);

    public double get_SmallChange() => Bridge.OnUiThread(() => Live.Range?.SmallChange ?? 0);

    public void ScrollIntoView() => Bridge.OnUiThread(() => Act(Live.ScrollIntoView()));

    /// <summary>A refused action is an error to UIA: the client learns it didn't
    /// happen — a disabled control, a read-only field.</summary>
    private static void Act(bool done)
    {
        if (!done) throw new InvalidOperationException("The element refused the action.");
    }
}

/// <summary>The provider of the form: the root of the fragment, hosted by the
/// window — UIA takes the window's own properties from the host provider.</summary>
[GeneratedComClass]
internal sealed partial class UiaRootProvider : UiaProvider, IRawElementProviderFragmentRoot
{
    public UiaRootProvider(UiaBridge bridge, AccessibilityPeer peer) : base(bridge, peer) { }

    public override nint get_HostRawElementProvider()
    {
        Marshal.ThrowExceptionForHR(UiaNative.UiaHostProviderFromHwnd(Bridge.Hwnd, out nint host));
        return host;
    }

    /// <summary>The root's bounds are the window's: UIA takes them from the host.</summary>
    public override UiaRect get_BoundingRectangle() => default;

    /// <summary>The deepest peer under the point — virtual parts included: a row
    /// of a grid, a tab, a menu item.</summary>
    public nint ElementProviderFromPoint(double x, double y) => Bridge.OnUiThread(() =>
    {
        Point point = Bridge.FromScreen(x, y);
        AccessibilityPeer hit = Peer;

        for (bool deeper = true; deeper;)
        {
            deeper = false;

            // the last child first: it is drawn on top
            IReadOnlyList<AccessibilityPeer> children = hit.Children;

            for (int i = children.Count - 1; i >= 0; i--)
            {
                if (Contains(children[i].Bounds, point))
                {
                    hit = children[i];
                    deeper = true;
                    break;
                }
            }
        }

        return Bridge.PointerFor(hit, typeof(IRawElementProviderFragment).GUID);
    });

    private static bool Contains(Rectangle bounds, Point point) =>
        point.X >= bounds.X && point.Y >= bounds.Y &&
        point.X < bounds.X + bounds.Width && point.Y < bounds.Y + bounds.Height;

    /// <summary>The focused element, or the current item inside it.</summary>
    public nint GetFocus() => Bridge.OnUiThread(() =>
    {
        if (Bridge.Form.FocusedElementForAccessibility?.GetAccessibilityPeer() is not { } focused)
            return 0;

        return Bridge.PointerFor(focused.FocusedDescendant ?? focused, typeof(IRawElementProviderFragment).GUID);
    });
}