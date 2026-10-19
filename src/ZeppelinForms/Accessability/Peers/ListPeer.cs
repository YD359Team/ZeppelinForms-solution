using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A list of items: ListBox, CheckedListBox, DragList. Its rows are
/// elements the item template built, and the list makes each a list item —
/// whatever control the template chose.</summary>
public class ListPeer : UIElementPeer, IAccessibilityChildFactory
{
    private readonly ItemsControl _items;

    public ListPeer(ItemsControl owner) : base(owner)
    {
        _items = owner;

        if (owner is ListBox list)
        {
            list.SelectionChanged += (_, _) =>
            {
                // the current row is what the keyboard works on: a screen reader
                // follows it as focus while the list itself has the focus
                if (FocusedDescendant is { } current)
                    AccessibilityEvents.RaiseFocusChanged(current);
            };
        }
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.List;

    protected override AccessibilityStates ControlStates =>
        _items is ListBox { SelectionMode: not SelectionMode.Single }
            ? AccessibilityStates.MultiSelectable
            : AccessibilityStates.None;

    /// <summary>The lead selected row, while the list has the focus.</summary>
    public override AccessibilityPeer? FocusedDescendant =>
        // the row's container — none while it is out of sight in a virtualizing list
        _items is ListBox { IsFocused: true, SelectedIndex: >= 0 } list
            ? list.ContainerFromIndex(list.SelectedIndex)?.GetAccessibilityPeer()
            : null;

    AccessibilityPeer? IAccessibilityChildFactory.CreatePeerForChild(UIElement child) =>
        new ListItemPeer(child, _items);
}

/// <summary>A row of a list: named by what the row shows, selected through the list.</summary>
public class ListItemPeer : UIElementPeer
{
    private readonly ItemsControl _list;

    public ListItemPeer(UIElement row, ItemsControl list) : base(row) => _list = list;

    protected override AccessibilityRole DefaultRole => AccessibilityRole.ListItem;

    protected override bool NameFromContent => true;

    /// <summary>The row's index among the items. Not its index in the children: a
    /// virtualizing list has children only for the rows in sight.</summary>
    private int Index => _list.IndexFromContainer(Owner);

    /// <summary>"Item 5 of 10 000": of all the items, not of the rows in sight.</summary>
    public override SetPosition? Position =>
        Index is >= 0 and var index ? new SetPosition(index + 1, _list.Items.Count) : null;

    protected override AccessibilityStates ControlStates
    {
        get
        {
            if (_list is not ListBox list) return AccessibilityStates.None;

            int index = Index;
            AccessibilityStates states = AccessibilityStates.Selectable;

            if (index >= 0 && list.IsSelected(index))
                states |= AccessibilityStates.Selected;

            if (list.IsFocused && index == list.SelectedIndex)
                states |= AccessibilityStates.Focused;

            if (list is CheckedListBox checkedList && index >= 0 && checkedList.IsChecked(index))
                states |= AccessibilityStates.Checked;

            return states;
        }
    }

    protected override AccessibilityActions ControlActions => _list switch
    {
        CheckedListBox => AccessibilityActions.Select | AccessibilityActions.Toggle | AccessibilityActions.ScrollIntoView,
        ListBox => AccessibilityActions.Select | AccessibilityActions.ScrollIntoView,
        _ => AccessibilityActions.ScrollIntoView,
    };

    /// <summary>The row's content is read as its name, so its texts are not separate
    /// children; only a row with something to operate in it — a button, a field —
    /// keeps its elements, or they would be out of reach.</summary>
    public override IReadOnlyList<AccessibilityPeer> Children =>
        HasInteractive(Owner) ? base.Children : [];

    private static bool HasInteractive(UIElement element)
    {
        foreach (UIElement child in ChildElements(element))
        {
            if (child is Forms.Interfaces.IInputElement || HasInteractive(child))
                return true;
        }

        return false;
    }

    public override bool Select()
    {
        if (_list is not ListBox list || !list.IsEffectivelyEnabled || Index < 0) return false;

        // as a click on the row: the only selected one, and the lead
        list.SelectedIndex = Index;
        return true;
    }

    public override bool Toggle()
    {
        if (_list is not CheckedListBox list || !list.IsEffectivelyEnabled || Index < 0) return false;

        list.ToggleChecked(Index);
        return true;
    }

    public override bool ScrollIntoView()
    {
        Drawing.Primitives.Point row = Owner.GetAbsolutePosition();
        Drawing.Primitives.Point list = _list.GetAbsolutePosition();

        // the row's offset in the content: its place in the viewport plus the scroll
        _list.ScrollTo(_list.ScrollX, row.Y - list.Y + _list.ScrollY);
        return true;
    }
}