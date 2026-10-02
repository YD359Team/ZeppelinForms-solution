using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>What the element is to an assistive technology. The properties are
/// plain, not styled: the meaning of an element is not a matter of theme.</summary>
public abstract partial class UIElement
{
    private AccessibilityPeer? _accessibilityPeer;
    private bool _accessibilityPeerCreated;

    /// <summary>The peer was made by the parent's peer — a list item of a list.</summary>
    private bool _accessibilityPeerFromParent;

    /// <summary>The name a screen reader says. Null — computed: from the label that
    /// names this element (<see cref="LabeledBy"/>), from its own text, from its
    /// tooltip. Needed where there is no text at all — a button with an icon only.</summary>
    public string? AccessibleName
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            RaiseAccessibilityChange(AccessibilityProperty.Name);
        }
    }

    /// <summary>A longer explanation, read after the name. Null — the tooltip,
    /// unless the tooltip already serves as the name.</summary>
    public string? AccessibleDescription
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            RaiseAccessibilityChange(AccessibilityProperty.Description);
        }
    }

    /// <summary>The element whose text names this one — the label in front of a
    /// text field. Label.Target sets it from the other side.</summary>
    public UIElement? LabeledBy
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;
            RaiseAccessibilityChange(AccessibilityProperty.Name);
        }
    }

    /// <summary>The role, where the control's own is wrong for its use here:
    /// a Border that is in fact a card list item, a Label that is a status line.
    /// Null — the control's own.</summary>
    public AccessibilityRole? AccessibleRole { get; set; }

    /// <summary>Leave the element and everything inside it out of the accessibility
    /// tree: decoration, a duplicate of what is said elsewhere.</summary>
    public bool IsAccessibilityHidden { get; set; }

    /// <summary>1 to 6 — the element is a heading of that level; screen readers jump
    /// between headings. 0 — not a heading.</summary>
    public int HeadingLevel
    {
        get;
        set => field = Math.Clamp(value, 0, 6);
    }

    /// <summary>Announce changes of this element's name or value without the user
    /// going to it: a status line, a counter, an error message.</summary>
    public AccessibilityLiveSetting LiveSetting { get; set; }

    /// <summary>The element's peer: created on the first request, then the same.
    /// Null for an element that is not in the accessibility tree at all.</summary>
    /// <remarks>
    /// The parent's peer is asked first: a list wraps its rows into list items,
    /// a menu its entries into menu items — the parent knows what a child is to it,
    /// the child alone doesn't.
    /// </remarks>
    public AccessibilityPeer? GetAccessibilityPeer()
    {
        if (_accessibilityPeerCreated) return _accessibilityPeer;

        _accessibilityPeerCreated = true;

        if (Parent?.GetAccessibilityPeer() is IAccessibilityChildFactory factory)
        {
            _accessibilityPeer = factory.CreatePeerForChild(this);
            _accessibilityPeerFromParent = true;
        }
        else
        {
            _accessibilityPeer = CreateAccessibilityPeer();
        }

        return _accessibilityPeer;
    }

    /// <summary>A new parent may see the element differently: a row moved out of
    /// a list is no longer a list item, a label moved into one becomes one. The
    /// peer is dropped where the parent made it or the new parent will; any other
    /// peer depends on the element alone and stays.</summary>
    private void OnParentChangedForAccessibility(UIElement? parent)
    {
        if (!_accessibilityPeerCreated) return;

        // the new parent's peer is asked for, not just looked up: this element had
        // a peer, so a bridge is reading the tree, and the parent's peer will be
        // needed for this element's anyway
        if (!_accessibilityPeerFromParent &&
            parent?.GetAccessibilityPeer() is not IAccessibilityChildFactory)
        {
            return;
        }

        _accessibilityPeer = null;
        _accessibilityPeerCreated = false;
        _accessibilityPeerFromParent = false;
    }

    /// <summary>Create this element's peer. Override for a control of your own;
    /// null — no peer, the element is not in the tree.</summary>
    protected virtual AccessibilityPeer? CreateAccessibilityPeer() =>
        AccessibilityPeerFactory.Create(this);

    /// <summary>The default peer, for a parent factory that has nothing special
    /// to make of a child.</summary>
    internal AccessibilityPeer? CreateDefaultAccessibilityPeer() => CreateAccessibilityPeer();

    /// <summary>Act as if clicked from the keyboard: the same path Space and Enter
    /// take, with the click in the element's center. Actions of peers that a click
    /// performs — pressing, toggling, opening — go through it, so the control's own
    /// logic runs and nothing is reimplemented on the side.</summary>
    internal bool PerformAccessibilityActivation()
    {
        if (!IsEffectivelyEnabled || !IsEffectivelyVisible) return false;

        Point absolute = GetAbsolutePosition();
        var center = new Point(
            absolute.X + ActualSize.Width / 2f,
            absolute.Y + ActualSize.Height / 2f);

        RaiseClick(new MouseClickEventArgs(MouseButton.Left, MouseButtonState.Up, center, 1));

        return true;
    }

    /// <summary>Report a change of the accessible description of this element —
    /// only where a peer exists, that is, where someone is listening.</summary>
    private protected void RaiseAccessibilityChange(AccessibilityProperty property)
    {
        if (_accessibilityPeer is not null)
            AccessibilityEvents.RaisePropertyChanged(_accessibilityPeer, property);
    }

    /// <summary>The peer if it exists, without creating one.</summary>
    internal AccessibilityPeer? ExistingAccessibilityPeer => _accessibilityPeer;
}