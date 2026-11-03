using Android.OS;
using Android.Views;
using Android.Views.Accessibility;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using NodeAction = Android.Views.Accessibility.AccessibilityNodeInfo.AccessibilityAction;

namespace ZeppelinForms.Android;

/// <summary>The forms on the surface as virtual views for TalkBack and the other
/// accessibility services: the provider the surface hands them, the finger's exploration
/// of the screen, and the events that keep them up to date.</summary>
/// <remarks>
/// <para>
/// One view draws everything, so to TalkBack the application was one opaque image: it
/// read the activity's label and nothing under the finger. A view that draws its own
/// content tells the services about it the way Android's own custom views do — an
/// AccessibilityNodeProvider of numbered virtual views, ExploreByTouchHelper's way without
/// AndroidX. The nodes and the decisions come from <see cref="VirtualViewTree"/> in the
/// core; this class copies them into AccessibilityNodeInfo and AccessibilityEvent.
/// </para>
/// <para>
/// Nothing is made until a service asks for a node: no peer, no subscription. When the
/// last service goes, the bridge stops listening.
/// </para>
/// <para>
/// Bounds move with layout, scrolling and animation, which report nothing: after a frame
/// the tree looks whether the nodes read have moved, at most four times a second and once
/// more after the last frame, and tells the services so.
/// </para>
/// </remarks>
internal sealed class AndroidAccessibility : AccessibilityNodeProvider
{
    /// <summary>How often the nodes are looked over for moves while frames go on.</summary>
    private const long ReviewIntervalMs = 250;

    // the AccessibilityEvent types, by their values: the enumeration's names are the
    // binding's, the values are Android's
    private const int TypeViewClicked = 0x00000001;
    private const int TypeViewFocused = 0x00000008;
    private const int TypeViewTextChanged = 0x00000010;
    private const int TypeWindowStateChanged = 0x00000020;
    private const int TypeViewHoverEnter = 0x00000080;
    private const int TypeViewHoverExit = 0x00000100;
    private const int TypeWindowContentChanged = 0x00000800;
    private const int TypeViewTextSelectionChanged = 0x00002000;
    private const int TypeViewAccessibilityFocused = 0x00008000;
    private const int TypeViewAccessibilityFocusCleared = 0x00010000;

    /// <summary>RangeInfo.RANGE_TYPE_FLOAT.</summary>
    private const int RangeTypeFloat = 1;

    // the arguments of ACTION_SET_TEXT and ACTION_SET_PROGRESS
    private const string ArgumentSetText = "ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE";
    private const string ArgumentProgress = "android.view.accessibility.action.ARGUMENT_PROGRESS_VALUE";

    private readonly AndroidPlatform _platform;
    private readonly ZeppelinView _view;
    private readonly AccessibilityManager? _manager;
    private readonly VirtualViewTree _tree;

    private bool _listening;
    private bool _flushPosted;
    private bool _reviewPending;
    private long _lastReview;

    /// <summary>The node under the exploring finger; NoId — none.</summary>
    private int _hovered = VirtualViewTree.NoId;

    public AndroidAccessibility(AndroidPlatform platform, ZeppelinView view)
    {
        _platform = platform;
        _view = view;
        _tree = new VirtualViewTree(platform.AccessibilityLayers);

        _manager = view.Context?.GetSystemService(global::Android.Content.Context.AccessibilityService) as AccessibilityManager;
        _manager?.AddAccessibilityStateChangeListener(new StateListener(this));
    }

    /// <summary>A service is on and explores by touch: the finger's moves come as hover
    /// events, and a double tap acts on the node in focus.</summary>
    private bool IsExploring => _manager is { IsEnabled: true, IsTouchExplorationEnabled: true };

    // ===== the nodes =====

    public override AccessibilityNodeInfo? CreateAccessibilityNodeInfo(int virtualViewId) =>
        App.RunGuarded(() => CreateNode(virtualViewId), whenHandled: null);

    private AccessibilityNodeInfo? CreateNode(int id)
    {
        StartListening();

        if (id == VirtualViewTree.HostId) return CreateHost();

        if (_tree.Describe(id) is not { } node) return null;

        AccessibilityNodeInfo info = ObtainNode(id);

        info.PackageName = _view.Context?.PackageName;
        info.ClassName = node.ClassName;

        if (node.Parent == VirtualViewTree.HostId)
            info.SetParent(_view);
        else
            info.SetParent(_view, node.Parent);

        foreach (int child in node.Children)
            info.AddChild(_view, child);

        SetBounds(info, node.Bounds);
        info.VisibleToUser = node.IsVisible;

        info.Text = node.Text;
        info.ContentDescription = node.ContentDescription;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            info.HintText = node.Hint;

        if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            info.PaneTitle = node.PaneTitle;
            info.Heading = node.IsHeading;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            info.StateDescription = node.StateDescription;

        info.Checkable = node.IsCheckable;
        SetChecked(info, node.IsChecked);
        info.Enabled = node.IsEnabled;
        info.Focusable = node.IsFocusable;
        info.Focused = node.IsFocused;
        info.Selected = node.IsSelected;
        info.Clickable = node.IsClickable;
        info.Editable = node.IsEditable;
        info.Password = node.IsPassword;
        info.MultiLine = node.IsMultiline;
        info.AccessibilityFocused = node.IsAccessibilityFocused;

        if (node.SelectionStart >= 0)
            info.SetTextSelection(node.SelectionStart, node.SelectionEnd);

        SetCollections(info, node);
        AddActions(info, node);

        return info;
    }

    /// <summary>The surface itself: the system's description of the view, the windows
    /// as its children.</summary>
    private AccessibilityNodeInfo CreateHost()
    {
#pragma warning disable CS0612, CS0618, CA1422 // Obtain is all API 24..29 have; it still works after
        AccessibilityNodeInfo info = AccessibilityNodeInfo.Obtain(_view)!;
#pragma warning restore CS0612, CS0618, CA1422

        _view.OnInitializeAccessibilityNodeInfo(info);

        foreach (int child in _tree.HostChildren())
            info.AddChild(_view, child);

        return info;
    }

    private AccessibilityNodeInfo ObtainNode(int id)
    {
#pragma warning disable CS0612, CS0618, CA1422 // Obtain is all API 24..29 have; it still works after
        return AccessibilityNodeInfo.Obtain(_view, id)!;
#pragma warning restore CS0612, CS0618, CA1422
    }

    /// <summary>The bounds in screen pixels, and in the surface's for the services that
    /// still read those.</summary>
    private void SetBounds(AccessibilityNodeInfo info, Rectangle bounds)
    {
        float scale = _platform.Scale;

        int left = (int)MathF.Floor(bounds.X * scale);
        int top = (int)MathF.Floor(bounds.Y * scale);
        int right = (int)MathF.Ceiling(bounds.Right * scale);
        int bottom = (int)MathF.Ceiling(bounds.Bottom * scale);

#pragma warning disable CS0612, CS0618, CA1422 // deprecated in API 29, read by older services
        info.SetBoundsInParent(new global::Android.Graphics.Rect(left, top, right, bottom));
#pragma warning restore CS0612, CS0618, CA1422

        int[] origin = new int[2];
        _view.GetLocationOnScreen(origin);

        info.SetBoundsInScreen(new global::Android.Graphics.Rect(
            left + origin[0], top + origin[1], right + origin[0], bottom + origin[1]));
    }

    private static void SetChecked(AccessibilityNodeInfo info, bool isChecked)
    {
#pragma warning disable CS0612, CS0618, CA1422 // Android 16 adds a three-state form; this one stays
        info.Checked = isChecked;
#pragma warning restore CS0612, CS0618, CA1422
    }

    private static void SetCollections(AccessibilityNodeInfo info, VirtualNode node)
    {
#pragma warning disable CS0612, CS0618, CA1422 // the constructors come in API 30; Obtain still works after
        if (node.Range is { } range)
        {
            info.SetRangeInfo(AccessibilityNodeInfo.RangeInfo.Obtain(
                (RangeType)RangeTypeFloat, (float)range.Minimum, (float)range.Maximum, (float)range.Value));
        }

        if (node.Collection is { } collection)
        {
            info.SetCollectionInfo(AccessibilityNodeInfo.CollectionInfo.Obtain(
                collection.Rows, collection.Columns, collection.Hierarchical));
        }

        if (node.ItemIndex >= 0 || node.IsHeaderItem)
        {
            info.SetCollectionItemInfo(AccessibilityNodeInfo.CollectionItemInfo.Obtain(
                Math.Max(0, node.ItemIndex), 1, 0, 1, node.IsHeaderItem, node.IsSelected));
        }
#pragma warning restore CS0612, CS0618, CA1422
    }

    private static void AddActions(AccessibilityNodeInfo info, VirtualNode node)
    {
        info.AddAction(node.IsAccessibilityFocused
            ? NodeAction.ActionClearAccessibilityFocus!
            : NodeAction.ActionAccessibilityFocus!);

        VirtualNodeActions actions = node.Actions;

        if (actions.HasFlag(VirtualNodeActions.Click)) info.AddAction(NodeAction.ActionClick!);
        if (actions.HasFlag(VirtualNodeActions.Focus)) info.AddAction(NodeAction.ActionFocus!);
        if (actions.HasFlag(VirtualNodeActions.Select)) info.AddAction(NodeAction.ActionSelect!);
        if (actions.HasFlag(VirtualNodeActions.Expand)) info.AddAction(NodeAction.ActionExpand!);
        if (actions.HasFlag(VirtualNodeActions.Collapse)) info.AddAction(NodeAction.ActionCollapse!);
        if (actions.HasFlag(VirtualNodeActions.SetText)) info.AddAction(NodeAction.ActionSetText!);
        if (actions.HasFlag(VirtualNodeActions.ScrollForward)) info.AddAction(NodeAction.ActionScrollForward!);
        if (actions.HasFlag(VirtualNodeActions.ScrollBackward)) info.AddAction(NodeAction.ActionScrollBackward!);
        if (actions.HasFlag(VirtualNodeActions.SetProgress)) info.AddAction(NodeAction.ActionSetProgress!);
        if (actions.HasFlag(VirtualNodeActions.ShowOnScreen)) info.AddAction(NodeAction.ActionShowOnScreen!);
    }

    // ===== actions =====

    public override bool PerformAction(int virtualViewId, global::Android.Views.Accessibility.Action action, Bundle? arguments) =>
        App.RunGuarded(() => Perform(virtualViewId, (int)action, arguments), whenHandled: true);

    private bool Perform(int id, int action, Bundle? arguments)
    {
        // the host's own actions are the view's: focusing the surface, its click
        if (id == VirtualViewTree.HostId)
            return _view.PerformAccessibilityAction((global::Android.Views.Accessibility.Action)action, arguments);

        bool done = ToVirtual(action) switch
        {
            VirtualAction.SetText => _tree.Perform(id, VirtualAction.SetText,
                text: arguments?.GetCharSequence(ArgumentSetText) ?? string.Empty),

            VirtualAction.SetProgress => arguments is not null &&
                _tree.Perform(id, VirtualAction.SetProgress, arguments.GetFloat(ArgumentProgress)),

            { } other => _tree.Perform(id, other),

            null => false,
        };

        // what the action told — the reader's focus moved, a click — is told now, not
        // after the next frame: TalkBack waits for it to speak
        Flush();

        return done;
    }

    /// <summary>An action's id as the tree's action; null — one the tree doesn't do.</summary>
    private static VirtualAction? ToVirtual(int action) =>
        action == NodeAction.ActionClick!.Id ? VirtualAction.Click
        : action == NodeAction.ActionAccessibilityFocus!.Id ? VirtualAction.AccessibilityFocus
        : action == NodeAction.ActionClearAccessibilityFocus!.Id ? VirtualAction.ClearAccessibilityFocus
        : action == NodeAction.ActionFocus!.Id ? VirtualAction.Focus
        : action == NodeAction.ActionSelect!.Id ? VirtualAction.Select
        : action == NodeAction.ActionExpand!.Id ? VirtualAction.Expand
        : action == NodeAction.ActionCollapse!.Id ? VirtualAction.Collapse
        : action == NodeAction.ActionSetText!.Id ? VirtualAction.SetText
        : action == NodeAction.ActionScrollForward!.Id ? VirtualAction.ScrollForward
        : action == NodeAction.ActionScrollBackward!.Id ? VirtualAction.ScrollBackward
        : action == NodeAction.ActionSetProgress!.Id ? VirtualAction.SetProgress
        : action == NodeAction.ActionShowOnScreen!.Id ? VirtualAction.ShowOnScreen
        : null;

    // ===== exploring by touch =====

    /// <summary>A hover event: with touch exploration on, the finger moving over the
    /// screen. The node under it is entered — TalkBack reads it — and the one it left is
    /// exited. False — not exploring, or the finger is on no node: the surface handles
    /// the event itself.</summary>
    internal bool HandleHover(MotionEvent e)
    {
        if (!IsExploring) return false;

        switch (e.ActionMasked)
        {
            case MotionEventActions.HoverEnter:
            case MotionEventActions.HoverMove:
                {
                    float scale = _platform.Scale;
                    int id = _tree.At(new Point(e.GetX() / scale, e.GetY() / scale));

                    if (id == VirtualViewTree.HostId) id = VirtualViewTree.NoId;

                    Hover(id);

                    return id != VirtualViewTree.NoId;
                }

            case MotionEventActions.HoverExit:
                if (_hovered == VirtualViewTree.NoId) return false;

                Hover(VirtualViewTree.NoId);
                return true;

            default:
                return false;
        }
    }

    private void Hover(int id)
    {
        if (id == _hovered) return;

        int left = _hovered;
        _hovered = id;

        // the new node first, as ExploreByTouchHelper sends them
        if (id != VirtualViewTree.NoId) Send(TypeViewHoverEnter, id);
        if (left != VirtualViewTree.NoId) Send(TypeViewHoverExit, left);
    }

    // ===== events =====

    private void StartListening()
    {
        if (_listening) return;

        _listening = true;

        AccessibilityEvents.FocusChanged += OnFocusChanged;
        AccessibilityEvents.PropertyChanged += OnPropertyChanged;
        AccessibilityEvents.StructureChanged += OnStructureChanged;
        AccessibilityEvents.Announcement += OnAnnouncement;

        // the windows as they are now: the first review tells only what changes after
        _tree.Review();
        _tree.TakeEvents();
    }

    private void StopListening()
    {
        if (!_listening) return;

        _listening = false;
        _hovered = VirtualViewTree.NoId;

        AccessibilityEvents.FocusChanged -= OnFocusChanged;
        AccessibilityEvents.PropertyChanged -= OnPropertyChanged;
        AccessibilityEvents.StructureChanged -= OnStructureChanged;
        AccessibilityEvents.Announcement -= OnAnnouncement;
    }

    private void OnFocusChanged(AccessibilityPeer peer)
    {
        _tree.OnFocusChanged(peer);
        ScheduleFlush();
    }

    private void OnPropertyChanged(AccessibilityPeer peer, AccessibilityProperty property)
    {
        _tree.OnPropertyChanged(peer, property);
        ScheduleFlush();
    }

    private void OnStructureChanged(AccessibilityPeer peer)
    {
        _tree.OnStructureChanged(peer);
        ScheduleFlush();
    }

    private void OnAnnouncement(Form form, string text, AccessibilityLiveSetting politeness)
    {
        _tree.OnAnnouncement(text);
        ScheduleFlush();
    }

    /// <summary>Tell the services once the step that changed things is over: the changes
    /// of one step are told together.</summary>
    private void ScheduleFlush()
    {
        if (_flushPosted) return;

        _flushPosted = true;
        _platform.Post(Flush);
    }

    /// <summary>After a frame: look over the windows and the bounds a little later.</summary>
    internal void AfterFrame()
    {
        if (!_listening || _reviewPending) return;

        _reviewPending = true;

        long wait = ReviewIntervalMs - (System.Environment.TickCount64 - _lastReview);

        if (wait <= 0)
            _platform.Post(Review);
        else
            _platform.PostDelayed(Review, wait);
    }

    private void Review()
    {
        _reviewPending = false;

        if (!_listening) return;

        _lastReview = System.Environment.TickCount64;
        _tree.Review();

        Flush();
    }

    private void Flush()
    {
        _flushPosted = false;

        IReadOnlyList<VirtualEvent> events = _tree.TakeEvents();

        if (!_listening || _manager is not { IsEnabled: true }) return;

        foreach (VirtualEvent e in events)
            Tell(e);
    }

    private void Tell(VirtualEvent e)
    {
        switch (e.Kind)
        {
            case VirtualEventKind.Focused:
                Send(TypeViewFocused, e.Id);
                break;

            case VirtualEventKind.AccessibilityFocused:
                Send(TypeViewAccessibilityFocused, e.Id);
                break;

            case VirtualEventKind.AccessibilityFocusCleared:
                Send(TypeViewAccessibilityFocusCleared, e.Id);
                break;

            case VirtualEventKind.Clicked:
                Send(TypeViewClicked, e.Id);
                break;

            case VirtualEventKind.ContentChanged:
                Send(TypeWindowContentChanged, e.Id, ev => ev.ContentChangeTypes = (ContentChangeTypes)(int)e.Changes);
                break;

            case VirtualEventKind.WindowStateChanged:
                Send(TypeWindowStateChanged, e.Id, ev =>
                {
                    ev.ContentChangeTypes = (ContentChangeTypes)(int)e.Changes;

                    if (e.Text is { Length: > 0 } title)
                    {
                        ev.Text?.Clear();
                        ev.Text?.Add(new Java.Lang.String(title));
                    }
                });
                break;

            case VirtualEventKind.TextChanged:
                Send(TypeViewTextChanged, e.Id, ev =>
                {
                    ev.BeforeText = e.BeforeText;
                    ev.FromIndex = e.From;
                    ev.RemovedCount = e.Removed;
                    ev.AddedCount = e.Added;
                });
                break;

            case VirtualEventKind.TextSelectionChanged:
                Send(TypeViewTextSelectionChanged, e.Id, ev =>
                {
                    ev.FromIndex = e.From;
                    ev.ToIndex = e.To;
                    ev.ItemCount = e.Count;
                });
                break;

            case VirtualEventKind.Announcement when e.Text is { Length: > 0 } text:
#pragma warning disable CS0612, CS0618, CA1422 // deprecated in API 36 for live regions; it still speaks
                _view.AnnounceForAccessibility(text);
#pragma warning restore CS0612, CS0618, CA1422
                break;
        }
    }

    /// <summary>Send an event of a node, filled the way ExploreByTouchHelper fills it: the
    /// node's class, words and states, with the host and the node as its source.</summary>
    private void Send(int type, int id, Action<AccessibilityEvent>? fill = null)
    {
        if (_view.Parent is not { } parent || _manager is not { IsEnabled: true }) return;

#pragma warning disable CS0612, CS0618, CA1422 // the constructor comes in API 30; Obtain still works after
        AccessibilityEvent? e = AccessibilityEvent.Obtain((EventTypes)type);
#pragma warning restore CS0612, CS0618, CA1422

        if (e is null) return;

        if (id == VirtualViewTree.HostId)
        {
            _view.OnInitializeAccessibilityEvent(e);
        }
        else
        {
            if (_tree.Describe(id) is not { } node) return;

            e.PackageName = _view.Context?.PackageName;
            e.ClassName = node.ClassName;
            e.ContentDescription = node.ContentDescription;
            e.Enabled = node.IsEnabled;
            e.Password = node.IsPassword;

#pragma warning disable CS0612, CS0618, CA1422 // Android 16 adds a three-state form; this one stays
            e.Checked = node.IsChecked;
#pragma warning restore CS0612, CS0618, CA1422

            if (node.Text is { Length: > 0 } text)
                e.Text?.Add(new Java.Lang.String(text));

            e.SetSource(_view, id);
        }

        fill?.Invoke(e);

        parent.RequestSendAccessibilityEvent(_view, e);
    }

    /// <summary>The services came or went: listening stops with the last one.</summary>
    private void OnStateChanged(bool enabled)
    {
        if (enabled) return;

        StopListening();
    }

    private sealed class StateListener(AndroidAccessibility owner)
        : Java.Lang.Object, AccessibilityManager.IAccessibilityStateChangeListener
    {
        public void OnAccessibilityStateChanged(bool enabled) =>
            App.RunGuarded(() => owner.OnStateChanged(enabled));
    }
}
