using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Charts;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Core.Globalization;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A titled group: a group box, named by its header.</summary>
public class GroupPeer(GroupBox owner) : UIElementPeer(owner)
{
    protected override AccessibilityRole DefaultRole => AccessibilityRole.Group;

    protected override bool NameFromContent => true;

    /// <summary>The header, not the texts inside: those are the group's children,
    /// and a group named after all of them would be read twice.</summary>
    protected override string? ContentName => owner.Header;
}

/// <summary>A collapsible section: named by its header, expanded and collapsed
/// through it.</summary>
public class SpoilerPeer(Spoiler owner) : UIElementPeer(owner)
{
    protected override AccessibilityRole DefaultRole => AccessibilityRole.Group;

    protected override bool NameFromContent => true;

    protected override string? ContentName => owner.Header;

    protected override AccessibilityStates ControlStates =>
        owner.IsCollapsed ? AccessibilityStates.Collapsed : AccessibilityStates.Expanded;

    protected override AccessibilityActions ControlActions =>
        owner.IsCollapsed ? AccessibilityActions.Expand : AccessibilityActions.Collapse;

    /// <summary>A collapsed section shows nothing of its content, and hides it
    /// from the reader as well.</summary>
    public override IReadOnlyList<AccessibilityPeer> Children =>
        owner.IsCollapsed ? [] : base.Children;

    public override bool Expand()
    {
        if (!owner.IsCollapsed || !owner.IsEffectivelyEnabled) return false;

        owner.IsCollapsed = false;
        RaisePropertyChanged(AccessibilityProperty.States);
        RaiseStructureChanged();
        return true;
    }

    public override bool Collapse()
    {
        if (owner.IsCollapsed || !owner.IsEffectivelyEnabled) return false;

        owner.IsCollapsed = true;
        RaisePropertyChanged(AccessibilityProperty.States);
        RaiseStructureChanged();
        return true;
    }
}

/// <summary>A picture: it has no text of its own, so its name is given explicitly
/// or by its tooltip. Nameless, it is decoration — the audit of the inspector
/// lists such images.</summary>
public class ImagePeer(UIElement owner) : UIElementPeer(owner)
{
    protected override AccessibilityRole DefaultRole => AccessibilityRole.Image;
}

/// <summary>A chart: an image named by its title.</summary>
public class ChartPeer(ChartBase owner) : UIElementPeer(owner)
{
    protected override AccessibilityRole DefaultRole => AccessibilityRole.Image;

    protected override bool NameFromContent => true;

    protected override string? ContentName => owner.Title;
}

/// <summary>A month calendar: its value is the selected date.</summary>
public class CalendarPeer : UIElementPeer
{
    private readonly Calendar _calendar;

    public CalendarPeer(Calendar owner) : base(owner)
    {
        _calendar = owner;
        owner.DateSelected += (_, _) => RaisePropertyChanged(AccessibilityProperty.Value);
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.Calendar;

    /// <summary>In the long form: "Friday, 2 October 2026" reads better than
    /// "02.10.2026", and says the weekday a sighted user sees in the grid.</summary>
    public override string? Value =>
        _calendar.SelectedDate?.ToString("D", _calendar.Culture ?? Localization.Culture);
}

/// <summary>A label with an explanation behind it: the explanation is its description.</summary>
public class HintLabelPeer(HintLabel owner) : TextPeer(owner)
{
    public override string? Description => owner.Hint ?? base.Description;

    protected override AccessibilityStates ControlStates =>
        owner.IsHintOpen ? AccessibilityStates.Expanded : AccessibilityStates.Collapsed;
}

/// <summary>A splitter between two panes.</summary>
public class SeparatorPeer(UIElement owner) : UIElementPeer(owner)
{
    protected override AccessibilityRole DefaultRole => AccessibilityRole.Separator;
}