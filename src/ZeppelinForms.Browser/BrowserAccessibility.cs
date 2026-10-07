using System.Runtime.CompilerServices;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Browser;

/// <summary>The ARIA mirror: the accessibility tree of the windows on the canvas,
/// kept as hidden DOM over it, so that screen readers find in the page what the
/// canvas draws.</summary>
/// <remarks>
/// <para>
/// A canvas is one opaque image to a screen reader. The mirror puts a transparent
/// element with a role and aria-* attributes over every peer, at its place — touch
/// exploration on a phone finds them by position — and the canvas, which keeps the
/// keyboard focus, points at the focused one with aria-activedescendant. The canvas
/// itself is role="application": screen readers pass the keys to the application,
/// whose keyboard model from 0.13 covers every control.
/// </para>
/// <para>
/// The tree is sent as a whole snapshot and the module applies it by ids, touching
/// only what changed. It is sent after a frame in which something reported a change,
/// and, while frames go on, at most four times a second otherwise — the bounds move
/// with layout and animation, which report nothing.
/// </para>
/// </remarks>
internal sealed class BrowserAccessibility
{
    /// <summary>How often bounds are refreshed while nothing reports a change.</summary>
    private const long IdleRefreshMs = 250;

    private readonly BrowserPlatform _platform;
    private readonly ConditionalWeakTable<AccessibilityPeer, string> _ids = new();
    private Dictionary<string, AccessibilityPeer> _peers = [];
    private int _nextId;
    private bool _dirty = true;
    private long _lastSent;

    public BrowserAccessibility(BrowserPlatform platform)
    {
        _platform = platform;

        AccessibilityEvents.FocusChanged += _ => MarkDirty();
        AccessibilityEvents.PropertyChanged += (_, _) => MarkDirty();
        AccessibilityEvents.StructureChanged += _ => MarkDirty();
        AccessibilityEvents.Announcement += OnAnnouncement;
    }

    /// <summary>Something changed: the next frame sends the tree. A frame is asked
    /// for, so that a change with nothing to repaint still reaches the reader.</summary>
    private void MarkDirty()
    {
        if (_dirty) return;

        _dirty = true;
        _platform.Invalidate();
    }

    /// <summary>After a painted frame: send the tree if it changed, or if the
    /// bounds are due for a refresh.</summary>
    public void AfterPaint(IReadOnlyList<BrowserWindow> windows)
    {
        long now = Environment.TickCount64;

        if (!_dirty && now - _lastSent < IdleRefreshMs) return;

        _dirty = false;
        _lastSent = now;

        var layers = new List<AriaSnapshot.Layer>(windows.Count);

        for (int i = 0; i < windows.Count; i++)
            layers.Add(new AriaSnapshot.Layer(windows[i].Form, windows[i].Origin, IsDialog: i > 0));

        var peers = new Dictionary<string, AccessibilityPeer>();
        string json = AriaSnapshot.Build(layers, IdOf, peers);

        _peers = peers;
        Interop.UpdateAccessibilityTree(json);
    }

    /// <summary>A stable id per peer for as long as it lives: the module keeps the
    /// DOM nodes by it, and aria-activedescendant names it.</summary>
    private string IdOf(AccessibilityPeer peer) =>
        _ids.GetValue(peer, _ => "zf-a11y-" + (++_nextId).ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>A screen reader acted on a node — a double tap on a phone, a click
    /// from the reader's virtual cursor: the peer's default action, the one its
    /// control does on a click.</summary>
    public void Act(string id, string action)
    {
        if (!_peers.TryGetValue(id, out AccessibilityPeer? peer)) return;

        if (action == "focus")
        {
            peer.Focus();
            return;
        }

        AccessibilityActions actions = peer.Actions;

        _ = (actions & AccessibilityActions.Invoke) != 0 ? peer.Invoke()
            : (actions & AccessibilityActions.Toggle) != 0 ? peer.Toggle()
            : (actions & AccessibilityActions.Select) != 0 ? peer.Select()
            : (actions & AccessibilityActions.Expand) != 0 ? peer.Expand()
            : (actions & AccessibilityActions.Collapse) != 0 ? peer.Collapse()
            : peer.Focus();
    }

    /// <summary>Form.Announce: into one of the two live regions of the mirror.</summary>
    private static void OnAnnouncement(Form form, string text, AccessibilityLiveSetting politeness) =>
        Interop.Announce(text, politeness == AccessibilityLiveSetting.Assertive);
}