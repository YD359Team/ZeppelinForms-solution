using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Accessibility;

/// <summary>A peer that decides what its children's elements are: a list makes its
/// rows list items, whatever control the item template built.</summary>
internal interface IAccessibilityChildFactory
{
    /// <summary>The peer for a direct child element; null — not in the tree.</summary>
    AccessibilityPeer? CreatePeerForChild(UIElement child);
}