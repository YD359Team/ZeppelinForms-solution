using System.Runtime.CompilerServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Accessibility;

/// <summary>The accessibility tree of the windows on one surface as a hierarchy of virtual
/// views: numbered nodes under a host view, as Android's AccessibilityNodeProvider gives
/// them to TalkBack.</summary>
/// <remarks>
/// <para>
/// Platform-neutral on purpose, as <see cref="AriaSnapshot"/> is: the Android bridge only
/// copies these nodes into AccessibilityNodeInfo and these events into AccessibilityEvent,
/// and the mapping — the part with the decisions in it — lives where it is tested with the
/// rest of the core. The class names and the change types are Android's, as the ARIA roles
/// of the snapshot are the browser's.
/// </para>
/// <para>
/// A peer gets a number the first time it is asked for and keeps it while it lives: a
/// screen reader keeps the nodes it has read by their numbers. Layout containers with no
/// role, no name and no action are no nodes — their children are lifted into the node
/// above, as the other bridges do; so is a nameless picture, which is decoration.
/// </para>
/// <para>
/// The windows of the surface are the host's children, the bottom one first. A window
/// under a modal dialog is out of the tree, as it is out of reach of the keyboard and of
/// the finger.
/// </para>
/// </remarks>
internal sealed class VirtualViewTree
{
    /// <summary>The host view itself: AccessibilityNodeProvider.HOST_VIEW_ID.</summary>
    public const int HostId = -1;

    /// <summary>No node: nothing under the finger, nothing in the reader's focus.</summary>
    public const int NoId = int.MinValue;

    /// <summary>A window on the surface: its form, its place, whether a modal dialog
    /// above keeps it out of reach.</summary>
    public readonly record struct Layer(Form Form, Point Origin, bool IsHidden);

    private readonly Func<IReadOnlyList<Layer>> _layers;
    private readonly ConditionalWeakTable<AccessibilityPeer, StrongBox<int>> _ids = new();
    private readonly Dictionary<int, WeakReference<AccessibilityPeer>> _peers = [];

    /// <summary>The bounds each node had when it was last described: the reader drew its
    /// focus around them, and found nodes under the finger by them.</summary>
    private readonly Dictionary<int, Rectangle> _described = [];

    /// <summary>The text of each field as last told: a text change is told against it.</summary>
    private readonly Dictionary<int, string> _texts = [];

    private readonly List<VirtualEvent> _events = [];

    /// <summary>The windows as last reviewed: a window opened or closed is told once.</summary>
    private List<(Form Form, bool IsHidden)> _shownLayers = [];

    private int _nextId;

    public VirtualViewTree(Func<IReadOnlyList<Layer>> layers) => _layers = layers;

    /// <summary>The node in the reader's focus — TalkBack's frame; <see cref="NoId"/> —
    /// none. Kept here, as the reader asks the tree for it and moves it by actions.</summary>
    public int AccessibilityFocus { get; private set; } = NoId;

    // ===== numbers =====

    public int IdOf(AccessibilityPeer peer)
    {
        StrongBox<int> box = _ids.GetValue(peer, _ => new StrongBox<int>(++_nextId));

        _peers[box.Value] = new WeakReference<AccessibilityPeer>(peer);

        return box.Value;
    }

    /// <summary>The peer of a node, while it is still in a window of the tree.</summary>
    public AccessibilityPeer? PeerOf(int id) =>
        _peers.TryGetValue(id, out WeakReference<AccessibilityPeer>? reference) &&
        reference.TryGetTarget(out AccessibilityPeer? peer) &&
        LayerOf(peer) is not null
            ? peer
            : null;

    // ===== the shape =====

    /// <summary>The host's children: the windows within reach, the bottom one first.</summary>
    public IReadOnlyList<int> HostChildren()
    {
        var ids = new List<int>();

        foreach (Layer layer in _layers())
        {
            if (!layer.IsHidden)
                ids.Add(IdOf(layer.Form.GetAccessibilityPeer()));
        }

        return ids;
    }

    /// <summary>Whether a peer is a node of its own rather than lifted into the one above.</summary>
    private static bool IsNode(AccessibilityPeer peer) => peer.Role switch
    {
        AccessibilityRole.None =>
            peer.Name.Length > 0 ||
            (peer.Actions & ~(AccessibilityActions.Focus | AccessibilityActions.ScrollIntoView)) != 0,

        // a nameless picture is decoration; separators and scroll bars TalkBack doesn't
        // read: two fingers scroll the panel under them
        AccessibilityRole.Image => peer.Name.Length > 0,
        AccessibilityRole.Separator or AccessibilityRole.ScrollBar => false,

        _ => true,
    };

    /// <summary>Whether a peer is left out together with what is in it.</summary>
    private static bool IsLeftOut(AccessibilityPeer peer) =>
        peer.Role is AccessibilityRole.Separator or AccessibilityRole.ScrollBar ||
        (peer.Role == AccessibilityRole.Image && peer.Name.Length == 0 && peer.Children.Count == 0);

    /// <summary>The nodes under a peer: its children, the containers among them replaced
    /// by what they hold.</summary>
    private static void NodesUnder(AccessibilityPeer peer, List<AccessibilityPeer> nodes)
    {
        foreach (AccessibilityPeer child in peer.Children)
        {
            if (IsLeftOut(child)) continue;

            if (IsNode(child))
                nodes.Add(child);
            else
                NodesUnder(child, nodes);
        }
    }

    /// <summary>The node a peer is read as: itself, or the nearest node above it.</summary>
    private static AccessibilityPeer? NodeOf(AccessibilityPeer peer)
    {
        for (AccessibilityPeer? node = peer; node is not null; node = node.Parent)
        {
            if (node.Parent is null || IsNode(node)) return node;
        }

        return null;
    }

    private Layer? LayerOf(AccessibilityPeer peer)
    {
        if (peer.Form is not { } form) return null;

        foreach (Layer layer in _layers())
        {
            if (ReferenceEquals(layer.Form, form))
                return layer.IsHidden ? null : layer;
        }

        return null;
    }

    /// <summary>A node as the reader reads it; null — there is no such node in the tree
    /// any more.</summary>
    public VirtualNode? Describe(int id)
    {
        if (PeerOf(id) is not { } peer || LayerOf(peer) is not { } layer) return null;

        bool isWindow = peer.Parent is null;

        // a window's parent is the host; any other node's, the nearest node above it —
        // its window at the farthest
        int parent = !isWindow && peer.Parent is { } above && NodeOf(above) is { } parentNode
            ? IdOf(parentNode)
            : HostId;

        var nodes = new List<AccessibilityPeer>();
        NodesUnder(peer, nodes);

        Rectangle bounds = VisibleBounds(peer, layer);
        _described[id] = bounds;

        VirtualNode node = Map(peer, isWindow);

        if (node.ClassName == EditText && peer.Value is { } text)
            _texts[id] = text;

        return node with
        {
            Id = id,
            Parent = parent,
            Children = [.. nodes.Select(IdOf)],
            Bounds = bounds,
            IsVisible = bounds.Width > 0 && bounds.Height > 0,
            IsAccessibilityFocused = id == AccessibilityFocus,
            Collection = Collection(peer, nodes),
        };
    }

    /// <summary>A peer's bounds on the surface, cut to what its ancestors show: a row
    /// scrolled out of its list is there, but not on the screen.</summary>
    private static Rectangle VisibleBounds(AccessibilityPeer peer, Layer layer)
    {
        Rectangle bounds = peer.Bounds;

        for (AccessibilityPeer? above = peer.Parent; above is not null; above = above.Parent)
            bounds = bounds.Intersect(above.Bounds);

        bounds = bounds.Intersect(new Rectangle(Point.Empty, layer.Form.ClientSize));

        return bounds.Offset(layer.Origin.X, layer.Origin.Y);
    }

    // ===== hit testing =====

    /// <summary>The node under a point of the surface — the finger exploring the screen:
    /// the deepest node there in the top window within reach; <see cref="HostId"/> —
    /// the point is in no window.</summary>
    public int At(Point point)
    {
        IReadOnlyList<Layer> layers = _layers();

        for (int i = layers.Count - 1; i >= 0; i--)
        {
            Layer layer = layers[i];

            if (layer.IsHidden) continue;

            var local = new Point(point.X - layer.Origin.X, point.Y - layer.Origin.Y);

            if (!new Rectangle(Point.Empty, layer.Form.ClientSize).Contains(local)) continue;

            AccessibilityPeer window = layer.Form.GetAccessibilityPeer();

            return IdOf(Deepest(window, local) ?? window);
        }

        return HostId;
    }

    /// <summary>The deepest node under a point among a peer's, the last one drawn first:
    /// an overlay is over the content it was added after.</summary>
    private static AccessibilityPeer? Deepest(AccessibilityPeer peer, Point point)
    {
        var nodes = new List<AccessibilityPeer>();
        NodesUnder(peer, nodes);

        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            AccessibilityPeer node = nodes[i];
            Rectangle bounds = node.Bounds;

            // a node of no size — a container sized by what it holds and holding it
            // outside — still has its nodes searched
            if (bounds.Width > 0 && bounds.Height > 0 && !bounds.Contains(point)) continue;

            if (Deepest(node, point) is { } deeper) return deeper;

            if (bounds.Width > 0 && bounds.Height > 0) return node;
        }

        return null;
    }

    // ===== actions =====

    /// <summary>Do what the reader asked of a node. False — the node can't, or is gone:
    /// TalkBack then taps the middle of the node itself.</summary>
    /// <param name="number">The new value of <see cref="VirtualAction.SetProgress"/>.</param>
    /// <param name="text">The new text of <see cref="VirtualAction.SetText"/>.</param>
    public bool Perform(int id, VirtualAction action, double number = 0, string? text = null)
    {
        if (action == VirtualAction.ClearAccessibilityFocus)
        {
            if (AccessibilityFocus != id) return false;

            AccessibilityFocus = NoId;
            Queue(new VirtualEvent(VirtualEventKind.AccessibilityFocusCleared, id));

            return true;
        }

        if (PeerOf(id) is not { } peer) return false;

        switch (action)
        {
            case VirtualAction.AccessibilityFocus:
                if (AccessibilityFocus == id) return false;

                if (AccessibilityFocus != NoId)
                    Queue(new VirtualEvent(VirtualEventKind.AccessibilityFocusCleared, AccessibilityFocus));

                AccessibilityFocus = id;

                // a row met reading on is brought into view, as a list does for its
                // focused row
                peer.ScrollIntoView();
                Queue(new VirtualEvent(VirtualEventKind.AccessibilityFocused, id));

                return true;

            case VirtualAction.Click:
                if (!Click(peer)) return false;

                // the reader says what the click did — "checked" — from this event
                Queue(new VirtualEvent(VirtualEventKind.Clicked, id));
                return true;

            case VirtualAction.Focus:
                return peer.Focus();

            case VirtualAction.Select:
                return peer.Select();

            case VirtualAction.Expand:
                return peer.Expand();

            case VirtualAction.Collapse:
                return peer.Collapse();

            case VirtualAction.SetText:
                return text is not null && peer.SetValue(text);

            case VirtualAction.ScrollForward or VirtualAction.ScrollBackward when peer.Range is { } range:
                {
                    double step = Step(range);
                    double value = action == VirtualAction.ScrollForward ? range.Value + step : range.Value - step;

                    return peer.SetRangeValue(Math.Clamp(value, range.Minimum, range.Maximum));
                }

            case VirtualAction.SetProgress when peer.Range is { } range:
                return peer.SetRangeValue(Math.Clamp(number, range.Minimum, range.Maximum));

            case VirtualAction.ShowOnScreen:
                return peer.ScrollIntoView();

            default:
                return false;
        }
    }

    /// <summary>A double tap: what a click on the control does.</summary>
    private static bool Click(AccessibilityPeer peer)
    {
        AccessibilityActions actions = peer.Actions;

        return (actions & AccessibilityActions.Invoke) != 0 ? peer.Invoke()
            : (actions & AccessibilityActions.Toggle) != 0 ? peer.Toggle()
            : (actions & AccessibilityActions.Select) != 0 ? peer.Select()
            : (actions & AccessibilityActions.Expand) != 0 && peer.States.HasFlag(AccessibilityStates.Collapsed) ? peer.Expand()
            : (actions & AccessibilityActions.Collapse) != 0 && peer.States.HasFlag(AccessibilityStates.Expanded) ? peer.Collapse()
            : peer.Focus();
    }

    /// <summary>One swipe of the reader over a slider: its small change, or a twentieth
    /// of its range where it has none.</summary>
    private static double Step(RangeInfo range) =>
        range.SmallChange > 0 ? range.SmallChange : (range.Maximum - range.Minimum) / 20;

    // ===== events =====

    /// <summary>The events since the last take, oldest first, the same change of a node
    /// told once.</summary>
    public IReadOnlyList<VirtualEvent> TakeEvents()
    {
        if (_events.Count == 0) return [];

        VirtualEvent[] events = [.. _events];
        _events.Clear();

        return events;
    }

    private void Queue(VirtualEvent e)
    {
        // a content change of a node already waiting to be told joins the waiting one
        if (e.Kind == VirtualEventKind.ContentChanged)
        {
            int index = _events.FindIndex(waiting => waiting.Kind == VirtualEventKind.ContentChanged && waiting.Id == e.Id);

            if (index >= 0)
            {
                _events[index] = _events[index] with { Changes = _events[index].Changes | e.Changes };
                return;
            }
        }

        _events.Add(e);
    }

    /// <summary>The keyboard's focus moved: to the current item of a list, a tree, a grid
    /// when it is in one.</summary>
    public void OnFocusChanged(AccessibilityPeer peer)
    {
        if (LayerOf(peer) is null) return;

        AccessibilityPeer focused = peer.FocusedDescendant ?? peer;

        if (NodeOf(focused) is { } node)
            Queue(new VirtualEvent(VirtualEventKind.Focused, IdOf(node)));
    }

    public void OnPropertyChanged(AccessibilityPeer peer, AccessibilityProperty property)
    {
        if (LayerOf(peer) is null || NodeOf(peer) is not { } node) return;

        int id = IdOf(node);

        switch (property)
        {
            case AccessibilityProperty.Name or AccessibilityProperty.Description:
                Queue(Content(id, VirtualChanges.Text | VirtualChanges.ContentDescription));
                break;

            case AccessibilityProperty.Value when ReferenceEquals(node, peer) && peer.Role == AccessibilityRole.TextBox:
                TellText(id, peer.Value ?? string.Empty);
                break;

            case AccessibilityProperty.Value or AccessibilityProperty.Range or AccessibilityProperty.States:
                Queue(Content(id, VirtualChanges.Text | VirtualChanges.StateDescription));
                break;

            case AccessibilityProperty.Caret when ReferenceEquals(node, peer) && peer.CaretIndex >= 0:
                {
                    (int start, int end) = Selection(peer);
                    Queue(new VirtualEvent(VirtualEventKind.TextSelectionChanged, id)
                    {
                        From = start,
                        To = end,
                        Count = peer.Value?.Length ?? 0,
                    });

                    break;
                }
        }

        // a live element is read out on its own when its name or value changes
        if (peer.LiveSetting != AccessibilityLiveSetting.Off &&
            property is AccessibilityProperty.Name or AccessibilityProperty.Value &&
            (property == AccessibilityProperty.Name ? peer.Name : peer.Value) is { Length: > 0 } spoken)
        {
            Queue(new VirtualEvent(VirtualEventKind.Announcement, id) { Text = spoken });
        }
    }

    /// <summary>A field's text changed: where, what went and how much came, against the
    /// text last told — TalkBack echoes the typed characters from it.</summary>
    private void TellText(int id, string text)
    {
        if (!_texts.TryGetValue(id, out string? before))
        {
            // never read: the reader has nothing to compare with
            _texts[id] = text;
            Queue(Content(id, VirtualChanges.Text));

            return;
        }

        _texts[id] = text;

        if (before == text) return;

        (int from, int removed, int added) = TextChange(before, text);

        Queue(new VirtualEvent(VirtualEventKind.TextChanged, id)
        {
            Text = text,
            BeforeText = before,
            From = from,
            Removed = removed,
            Added = added,
        });
    }

    /// <summary>The one edit that turns one text into another: the common beginning and
    /// end are kept, what is between them was replaced.</summary>
    internal static (int From, int Removed, int Added) TextChange(string before, string after)
    {
        int prefix = 0;
        int limit = Math.Min(before.Length, after.Length);

        while (prefix < limit && before[prefix] == after[prefix])
            prefix++;

        int suffix = 0;

        while (suffix < limit - prefix &&
               before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
        {
            suffix++;
        }

        return (prefix, before.Length - prefix - suffix, after.Length - prefix - suffix);
    }

    public void OnStructureChanged(AccessibilityPeer peer)
    {
        if (LayerOf(peer) is null || NodeOf(peer) is not { } node) return;

        Queue(Content(IdOf(node), VirtualChanges.Subtree));
    }

    public void OnAnnouncement(string text)
    {
        if (text.Length > 0)
            Queue(new VirtualEvent(VirtualEventKind.Announcement, HostId) { Text = text });
    }

    private static VirtualEvent Content(int id, VirtualChanges changes) =>
        new(VirtualEventKind.ContentChanged, id) { Changes = changes };

    /// <summary>After a frame: tell the windows that came and went, and whether the nodes
    /// the reader has read moved — scrolled, laid out anew, sliding in. Nothing reports a
    /// move, so it is looked for; call it a few times a second at most.</summary>
    public void Review()
    {
        IReadOnlyList<Layer> layers = _layers();
        List<(Form Form, bool IsHidden)> shown = [.. layers.Select(layer => (layer.Form, layer.IsHidden))];

        if (!shown.SequenceEqual(_shownLayers))
        {
            // a window newly within reach is announced by its title, as a pane appearing
            foreach (Layer layer in layers)
            {
                if (!layer.IsHidden && !_shownLayers.Contains((layer.Form, false)))
                {
                    AccessibilityPeer window = layer.Form.GetAccessibilityPeer();

                    Queue(new VirtualEvent(VirtualEventKind.WindowStateChanged, IdOf(window))
                    {
                        Changes = VirtualChanges.PaneAppeared,
                        Text = window.Name,
                    });
                }
            }

            _shownLayers = shown;

            if (AccessibilityFocus != NoId && PeerOf(AccessibilityFocus) is null)
                AccessibilityFocus = NoId;

            Queue(Content(HostId, VirtualChanges.Subtree));
        }

        if (BoundsMoved())
            Queue(Content(HostId, VirtualChanges.Subtree));
    }

    /// <summary>Whether a node the reader has read is elsewhere now; the nodes gone are
    /// forgotten on the way.</summary>
    private bool BoundsMoved()
    {
        bool moved = false;
        List<int>? gone = null;

        foreach ((int id, Rectangle described) in _described)
        {
            if (PeerOf(id) is not { } peer || LayerOf(peer) is not { } layer)
            {
                (gone ??= []).Add(id);
                continue;
            }

            Rectangle now = VisibleBounds(peer, layer);

            if (now != described)
            {
                moved = true;
                _described[id] = now;
            }
        }

        if (gone is not null)
        {
            foreach (int id in gone)
            {
                _described.Remove(id);
                _texts.Remove(id);

                if (_peers.TryGetValue(id, out WeakReference<AccessibilityPeer>? reference) &&
                    !reference.TryGetTarget(out _))
                {
                    _peers.Remove(id);
                }
            }
        }

        return moved;
    }

    // ===== the mapping =====

    internal const string View = "android.view.View";
    internal const string ViewGroup = "android.view.ViewGroup";
    internal const string TextView = "android.widget.TextView";
    internal const string EditText = "android.widget.EditText";

    /// <summary>A peer as a node: its class — which TalkBack reads the role from — its
    /// text, states and actions. The place in the tree is the caller's.</summary>
    internal static VirtualNode Map(AccessibilityPeer peer, bool isWindow = false)
    {
        AccessibilityStates states = peer.States;
        AccessibilityActions actions = peer.Actions;
        string name = peer.Name;
        bool enabled = !states.HasFlag(AccessibilityStates.Disabled);

        string className = isWindow ? ViewGroup : peer.Role switch
        {
            AccessibilityRole.Text or AccessibilityRole.Heading or AccessibilityRole.Link or
                AccessibilityRole.ToolTip => TextView,
            AccessibilityRole.Image => "android.widget.ImageView",
            AccessibilityRole.Button or AccessibilityRole.SplitButton => "android.widget.Button",
            AccessibilityRole.ToggleButton => "android.widget.ToggleButton",
            AccessibilityRole.CheckBox => "android.widget.CheckBox",
            AccessibilityRole.RadioButton => "android.widget.RadioButton",
            AccessibilityRole.Switch => "android.widget.Switch",
            AccessibilityRole.TextBox => EditText,
            AccessibilityRole.SpinButton or AccessibilityRole.Slider => "android.widget.SeekBar",
            AccessibilityRole.ProgressBar => "android.widget.ProgressBar",
            AccessibilityRole.ComboBox => "android.widget.Spinner",
            AccessibilityRole.List or AccessibilityRole.Menu or AccessibilityRole.MenuBar => "android.widget.ListView",
            AccessibilityRole.Tree => "android.widget.ExpandableListView",
            AccessibilityRole.Table => "android.widget.GridView",
            AccessibilityRole.TabList => "android.widget.TabWidget",
            AccessibilityRole.Tab => "android.app.ActionBar$Tab",
            AccessibilityRole.Window or AccessibilityRole.Dialog or AccessibilityRole.Group or
                AccessibilityRole.TabPanel or AccessibilityRole.Calendar => ViewGroup,
            _ => View,
        };

        bool isText = className == TextView;
        bool isEdit = className == EditText;
        bool editable = isEdit && enabled && !states.HasFlag(AccessibilityStates.ReadOnly) &&
                        (actions & AccessibilityActions.SetValue) != 0;

        bool checkable = peer.Role is AccessibilityRole.CheckBox or AccessibilityRole.RadioButton or
                         AccessibilityRole.Switch or AccessibilityRole.ToggleButton;

        bool clickable = enabled &&
            (actions & (AccessibilityActions.Invoke | AccessibilityActions.Toggle | AccessibilityActions.Select |
                        AccessibilityActions.Expand | AccessibilityActions.Collapse)) != 0;

        // the words: a text is read by what it says, a field by what is in it with its
        // label as the hint, anything else by its name
        string? text = null;
        string? description = null;
        string? hint = peer.Description;

        if (isWindow)
        {
            // a window says its title as a pane, not as its content: read as content,
            // the title would come before every node a finger touches in it
        }
        else if (isText)
        {
            text = name.Length > 0 ? name : peer.Value;
        }
        else if (isEdit || peer.Role == AccessibilityRole.ComboBox)
        {
            text = peer.Value;
            hint = name.Length > 0 ? (hint is { Length: > 0 } more ? $"{name}, {more}" : name) : hint;

            if (peer.Role == AccessibilityRole.ComboBox) description = name.Length > 0 ? name : null;
        }
        else if (name.Length > 0)
        {
            description = name;
        }

        RangeInfo? range = states.HasFlag(AccessibilityStates.Busy) ? null : peer.Range;

        // a range's value as the user reads it — "40 %", "Medium" — over the bare number
        string? stateDescription = range is not null && !isEdit && peer.Value is { Length: > 0 } valueText
            ? valueText
            : null;

        VirtualNodeActions nodeActions = VirtualNodeActions.None;

        if (clickable) nodeActions |= VirtualNodeActions.Click;

        if ((actions & AccessibilityActions.Focus) != 0 && enabled && !states.HasFlag(AccessibilityStates.Focused))
            nodeActions |= VirtualNodeActions.Focus;

        if ((actions & AccessibilityActions.Select) != 0 && !states.HasFlag(AccessibilityStates.Selected))
            nodeActions |= VirtualNodeActions.Select;

        if ((actions & AccessibilityActions.Expand) != 0 && states.HasFlag(AccessibilityStates.Collapsed))
            nodeActions |= VirtualNodeActions.Expand;

        if ((actions & AccessibilityActions.Collapse) != 0 && states.HasFlag(AccessibilityStates.Expanded))
            nodeActions |= VirtualNodeActions.Collapse;

        if (editable) nodeActions |= VirtualNodeActions.SetText;

        bool adjustable = range is not null && enabled && !isEdit &&
                          (actions & AccessibilityActions.SetValue) != 0 &&
                          peer.Role is not AccessibilityRole.ProgressBar;

        if (adjustable)
        {
            nodeActions |= VirtualNodeActions.SetProgress;

            if (range!.Value.Value < range.Value.Maximum) nodeActions |= VirtualNodeActions.ScrollForward;
            if (range.Value.Value > range.Value.Minimum) nodeActions |= VirtualNodeActions.ScrollBackward;
        }

        if ((actions & AccessibilityActions.ScrollIntoView) != 0) nodeActions |= VirtualNodeActions.ShowOnScreen;

        (int selectionStart, int selectionEnd) = isEdit && peer.CaretIndex >= 0 ? Selection(peer) : (-1, -1);

        return new VirtualNode
        {
            ClassName = className,
            Text = text,
            ContentDescription = description,
            Hint = hint is { Length: > 0 } ? hint : null,
            StateDescription = stateDescription,
            PaneTitle = isWindow && name.Length > 0 ? name : null,
            IsHeading = peer.Role == AccessibilityRole.Heading || peer.HeadingLevel > 0,
            IsCheckable = checkable,
            IsChecked = checkable && states.HasFlag(AccessibilityStates.Checked),
            IsEnabled = enabled,
            IsFocusable = states.HasFlag(AccessibilityStates.Focusable),
            IsFocused = states.HasFlag(AccessibilityStates.Focused),
            IsSelected = states.HasFlag(AccessibilityStates.Selected),
            IsClickable = clickable,
            IsEditable = editable,
            IsPassword = states.HasFlag(AccessibilityStates.Protected),
            IsMultiline = states.HasFlag(AccessibilityStates.Multiline),
            IsDialog = peer.Role == AccessibilityRole.Dialog,
            Range = range,
            ItemIndex = peer.Position is { } position ? position.Index - 1 : -1,
            IsHeaderItem = peer.Role == AccessibilityRole.ColumnHeader,
            SelectionStart = selectionStart,
            SelectionEnd = selectionEnd,
            Actions = nodeActions,
            LiveSetting = peer.LiveSetting,
        };
    }

    /// <summary>A collection's rows and columns: a list's items, a grid's rows and the
    /// cells of its first one.</summary>
    private static (int Rows, int Columns, bool Hierarchical)? Collection(AccessibilityPeer peer, List<AccessibilityPeer> nodes) =>
        peer.Role switch
        {
            AccessibilityRole.List or AccessibilityRole.Menu or AccessibilityRole.MenuBar or AccessibilityRole.TabList =>
                (nodes.Count, 1, false),
            AccessibilityRole.Tree => (nodes.Count, 1, true),
            AccessibilityRole.Table => (
                nodes.Count(node => node.Role == AccessibilityRole.Row),
                Math.Max(1, nodes.FirstOrDefault(node => node.Role == AccessibilityRole.Row)?.Children.Count ?? 1),
                false),
            _ => null,
        };

    /// <summary>A field's selection, or its caret as an empty one.</summary>
    private static (int Start, int End) Selection(AccessibilityPeer peer)
    {
        int start = peer.SelectionStart >= 0 ? peer.SelectionStart : peer.CaretIndex;

        return (start, start + Math.Max(0, peer.SelectionLength));
    }
}

/// <summary>A node of <see cref="VirtualViewTree"/>, as AccessibilityNodeInfo has it.</summary>
internal sealed record VirtualNode
{
    public int Id { get; init; }

    /// <summary>The parent node, or <see cref="VirtualViewTree.HostId"/>.</summary>
    public int Parent { get; init; } = VirtualViewTree.HostId;

    public IReadOnlyList<int> Children { get; init; } = [];

    /// <summary>Where the node is on the surface, in logical pixels, cut to what is shown.</summary>
    public Rectangle Bounds { get; init; }

    public bool IsVisible { get; init; }

    public required string ClassName { get; init; }

    public string? Text { get; init; }

    public string? ContentDescription { get; init; }

    public string? Hint { get; init; }

    public string? StateDescription { get; init; }

    public string? PaneTitle { get; init; }

    public bool IsHeading { get; init; }

    public bool IsCheckable { get; init; }

    public bool IsChecked { get; init; }

    public bool IsEnabled { get; init; }

    public bool IsFocusable { get; init; }

    public bool IsFocused { get; init; }

    public bool IsSelected { get; init; }

    public bool IsClickable { get; init; }

    public bool IsEditable { get; init; }

    public bool IsPassword { get; init; }

    public bool IsMultiline { get; init; }

    public bool IsDialog { get; init; }

    public bool IsAccessibilityFocused { get; init; }

    public RangeInfo? Range { get; init; }

    /// <summary>The rows and columns of a collection: a list, a tree, a grid.</summary>
    public (int Rows, int Columns, bool Hierarchical)? Collection { get; init; }

    /// <summary>The place of an item in its collection, from 0; −1 — none.</summary>
    public int ItemIndex { get; init; } = -1;

    public bool IsHeaderItem { get; init; }

    public int SelectionStart { get; init; } = -1;

    public int SelectionEnd { get; init; } = -1;

    public VirtualNodeActions Actions { get; init; }

    public AccessibilityLiveSetting LiveSetting { get; init; }
}

/// <summary>What a node offers the reader, besides moving its own focus to it.</summary>
[Flags]
internal enum VirtualNodeActions
{
    None = 0,
    Click = 1 << 0,
    Focus = 1 << 1,
    Select = 1 << 2,
    Expand = 1 << 3,
    Collapse = 1 << 4,
    SetText = 1 << 5,
    ScrollForward = 1 << 6,
    ScrollBackward = 1 << 7,
    SetProgress = 1 << 8,
    ShowOnScreen = 1 << 9,
}

/// <summary>An action the reader asks of a node.</summary>
internal enum VirtualAction
{
    Click,
    Focus,
    Select,
    Expand,
    Collapse,
    SetText,
    ScrollForward,
    ScrollBackward,
    SetProgress,
    ShowOnScreen,
    AccessibilityFocus,
    ClearAccessibilityFocus,
}

internal enum VirtualEventKind
{
    /// <summary>The keyboard's focus: TYPE_VIEW_FOCUSED.</summary>
    Focused,

    /// <summary>TYPE_VIEW_ACCESSIBILITY_FOCUSED.</summary>
    AccessibilityFocused,

    /// <summary>TYPE_VIEW_ACCESSIBILITY_FOCUS_CLEARED.</summary>
    AccessibilityFocusCleared,

    /// <summary>TYPE_VIEW_CLICKED.</summary>
    Clicked,

    /// <summary>TYPE_WINDOW_CONTENT_CHANGED with <see cref="VirtualEvent.Changes"/>.</summary>
    ContentChanged,

    /// <summary>TYPE_WINDOW_STATE_CHANGED: a window came within reach.</summary>
    WindowStateChanged,

    /// <summary>TYPE_VIEW_TEXT_CHANGED.</summary>
    TextChanged,

    /// <summary>TYPE_VIEW_TEXT_SELECTION_CHANGED.</summary>
    TextSelectionChanged,

    /// <summary>Text read out without moving the focus: TYPE_ANNOUNCEMENT.</summary>
    Announcement,
}

/// <summary>What changed in a node: AccessibilityEvent's CONTENT_CHANGE_TYPE_* bits, with
/// their values.</summary>
[Flags]
internal enum VirtualChanges
{
    Undefined = 0,
    Subtree = 1,
    Text = 2,
    ContentDescription = 4,
    PaneTitle = 8,
    PaneAppeared = 16,
    PaneDisappeared = 32,
    StateDescription = 64,
}

internal readonly record struct VirtualEvent(VirtualEventKind Kind, int Id)
{
    public VirtualChanges Changes { get; init; }

    /// <summary>The text of a field after a change, a pane's title, an announcement.</summary>
    public string? Text { get; init; }

    public string? BeforeText { get; init; }

    /// <summary>Where a text change starts; a selection's start.</summary>
    public int From { get; init; }

    /// <summary>A selection's end.</summary>
    public int To { get; init; }

    /// <summary>The length of the text a selection is in.</summary>
    public int Count { get; init; }

    public int Removed { get; init; }

    public int Added { get; init; }
}
