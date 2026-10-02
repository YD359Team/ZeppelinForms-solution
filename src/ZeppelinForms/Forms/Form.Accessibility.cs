using ZeppelinForms.Accessibility;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms;

/// <summary>The form as the root of the accessibility tree, and its announcements.</summary>
public partial class Form
{
    private FormPeer? _accessibilityPeer;

    /// <summary>The root of the form's accessibility tree.</summary>
    public AccessibilityPeer GetAccessibilityPeer() => _accessibilityPeer ??= new FormPeer(this);

    /// <summary>Have a screen reader say this without moving the focus: "Saved",
    /// "3 results", "Connection lost".</summary>
    /// <param name="politeness">Polite waits for what is being said to end;
    /// Assertive interrupts it — for errors only. Off says nothing.</param>
    /// <remarks>Without an assistive technology nothing happens: the announcement
    /// goes only to the bridges listening for it.</remarks>
    public void Announce(string text, AccessibilityLiveSetting politeness = AccessibilityLiveSetting.Polite)
    {
        if (string.IsNullOrWhiteSpace(text) || politeness == AccessibilityLiveSetting.Off) return;

        AccessibilityEvents.RaiseAnnouncement(this, text, politeness);
    }

    /// <summary>Move the focus to an element on a peer's request.</summary>
    internal bool FocusForAccessibility(UIElement element) =>
        element.FindOwner() == this && _focusDispatcher.FocusElement(element);

    /// <summary>Report the new focus. Inside a list, a tree or a grid the focus is
    /// reported on its current item: that is what the user moves through.</summary>
    private void OnFocusChangedForAccessibility(object? sender, UIElement? element)
    {
        if (element is null || !AccessibilityEvents.IsListening) return;

        if (element.GetAccessibilityPeer() is { } peer)
            AccessibilityEvents.RaiseFocusChanged(peer.FocusedDescendant ?? peer);
    }

    /// <summary>An overlay came or went: the form's children changed.</summary>
    private void RaiseOverlaysChanged()
    {
        if (_accessibilityPeer is not null)
            AccessibilityEvents.RaiseStructureChanged(_accessibilityPeer);
    }
}