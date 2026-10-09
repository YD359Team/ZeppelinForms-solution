using System.Collections.Specialized;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Accessibility;

/// <summary>The peer of an element: the common part — name, states, children,
/// bounds, focus — computed from the element itself. Control peers derive from it
/// and add their role, value and actions.</summary>
public class UIElementPeer : AccessibilityPeer
{
    public UIElementPeer(UIElement owner)
    {
        Owner = owner;

        // the children of a panel change with its collection, which is public:
        // the peer listens there, and the panel itself needs no change for it
        if (owner is PanelControl panel)
            panel.Children.CollectionChanged += OnChildrenChanged;
    }

    public UIElement Owner { get; }

    public override UIElement? Element => Owner;

    /// <summary>The control's own role; the element's AccessibleRole and
    /// HeadingLevel take precedence over it.</summary>
    protected virtual AccessibilityRole DefaultRole => AccessibilityRole.None;

    public sealed override AccessibilityRole Role =>
        Owner.AccessibleRole
        ?? (Owner.HeadingLevel > 0 ? AccessibilityRole.Heading : DefaultRole);

    /// <summary>Whether the element is named by what it shows: a button by its
    /// caption, a list item by its text. Not a text field — its text is the value,
    /// and a field named by what was typed into it would be read twice.</summary>
    protected virtual bool NameFromContent => false;

    /// <summary>The name taken from the content, where <see cref="NameFromContent"/>.</summary>
    protected virtual string? ContentName => AccessibilityText.Of(Owner);

    /// <summary>The name by precedence: set explicitly, given by a label, shown
    /// by the element itself, its tooltip. A text field without a label is named
    /// by its tooltip rather than left nameless.</summary>
    public sealed override string Name
    {
        get
        {
            if (Owner.AccessibleName is { Length: > 0 } name) return name;

            if (Owner.LabeledBy is { } label && AccessibilityText.Of(label) is { Length: > 0 } labelled)
                return labelled;

            if ((NameFromContent || Role is AccessibilityRole.Text or AccessibilityRole.Heading) &&
                ContentName is { Length: > 0 } content)
                return content;

            return Owner.ToolTip ?? string.Empty;
        }
    }

    /// <summary>The explicit description, or the tooltip where it isn't
    /// already the name — said once is enough.</summary>
    public override string? Description =>
        Owner.AccessibleDescription
        ?? (Owner.ToolTip is { Length: > 0 } tip && tip != Name ? tip : null);

    /// <summary>The states every element has — focus and availability — plus
    /// the control's own, see <see cref="ControlStates"/>.</summary>
    public sealed override AccessibilityStates States
    {
        get
        {
            AccessibilityStates states = ControlStates;

            if (Owner is IInputElement input)
            {
                if (input.TabStop) states |= AccessibilityStates.Focusable;
                if (input.IsFocused) states |= AccessibilityStates.Focused;
            }

            if (!Owner.IsEffectivelyEnabled) states |= AccessibilityStates.Disabled;

            return states;
        }
    }

    protected virtual AccessibilityStates ControlStates => AccessibilityStates.None;

    /// <summary>Focus where the element takes it, plus the control's own
    /// actions, see <see cref="ControlActions"/>.</summary>
    public sealed override AccessibilityActions Actions =>
        ControlActions | (Owner is IInputElement { TabStop: true } ? AccessibilityActions.Focus : 0);

    protected virtual AccessibilityActions ControlActions => AccessibilityActions.None;

    public override int HeadingLevel => Owner.HeadingLevel;

    public override AccessibilityLiveSetting LiveSetting => Owner.LiveSetting;

    public override string? AccessKey => Owner.AccessKey is { } key ? $"Alt+{key}" : null;

    public override AccessibilityPeer? Parent =>
        Owner.Parent is { } parent
            ? parent.GetAccessibilityPeer()
            : Owner.FindOwner()?.GetAccessibilityPeer();

    public override IReadOnlyList<AccessibilityPeer> Children => ElementChildren(Owner);

    public override Rectangle Bounds => new(Owner.GetAbsolutePosition(), Owner.ActualSize);

    public override Form? Form => Owner.FindOwner();

    public override bool Focus() =>
        Owner is IInputElement && Form?.FocusForAccessibility(Owner) == true;

    // ===== tree helpers =====

    /// <summary>The element's own children, in the order they are read.</summary>
    internal static IEnumerable<UIElement> ChildElements(UIElement element) => element switch
    {
        WrapControl { Child: { } child } => [child],
        PanelControl panel => panel.NavigationOrder,
        _ => [],
    };

    /// <summary>The peers of the children that are in the tree: visible, not
    /// hidden, and with a peer at all — decoration has none.</summary>
    internal static IReadOnlyList<AccessibilityPeer> ElementChildren(UIElement element)
    {
        var peers = new List<AccessibilityPeer>();

        foreach (UIElement child in ChildElements(element))
        {
            if (!child.IsVisible || child.IsAccessibilityHidden) continue;

            if (child.GetAccessibilityPeer() is { } peer)
                peers.Add(peer);
        }

        return peers;
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RaiseStructureChanged();
}