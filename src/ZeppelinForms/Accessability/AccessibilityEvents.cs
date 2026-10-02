using ZeppelinForms.Forms;

namespace ZeppelinForms.Accessibility;

/// <summary>What changed for assistive technologies: focus, properties, structure,
/// announcements. Platform bridges subscribe; controls and peers raise.</summary>
/// <remarks>
/// <para>
/// Raising is cheap when nobody listens: every Raise checks for subscribers first,
/// and most changes are raised by peers, which exist only once a bridge asked for
/// them. An application without a screen reader pays a null check per focus change.
/// </para>
/// <para>
/// Raised on the UI thread, like everything that changes the element tree. A bridge
/// whose platform wants notifications elsewhere marshals them itself.
/// </para>
/// </remarks>
public static class AccessibilityEvents
{
    /// <summary>Keyboard focus moved to this peer — an element, or the current item
    /// inside a focused list, tree or grid.</summary>
    public static event Action<AccessibilityPeer>? FocusChanged;

    public static event Action<AccessibilityPeer, AccessibilityProperty>? PropertyChanged;

    /// <summary>The peer's children changed: added, removed, reordered.</summary>
    public static event Action<AccessibilityPeer>? StructureChanged;

    /// <summary>Text to announce without moving the focus, see <see cref="Form.Announce"/>.</summary>
    public static event Action<Form, string, AccessibilityLiveSetting>? Announcement;

    /// <summary>Whether anybody listens. Code that would build something only to
    /// report it checks this first.</summary>
    public static bool IsListening =>
        FocusChanged is not null || PropertyChanged is not null ||
        StructureChanged is not null || Announcement is not null;

    internal static void RaiseFocusChanged(AccessibilityPeer peer) => FocusChanged?.Invoke(peer);

    internal static void RaisePropertyChanged(AccessibilityPeer peer, AccessibilityProperty property) =>
        PropertyChanged?.Invoke(peer, property);

    internal static void RaiseStructureChanged(AccessibilityPeer peer) => StructureChanged?.Invoke(peer);

    internal static void RaiseAnnouncement(Form form, string text, AccessibilityLiveSetting politeness) =>
        Announcement?.Invoke(form, text, politeness);
}