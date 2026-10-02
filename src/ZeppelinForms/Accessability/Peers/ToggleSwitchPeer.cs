using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>An on/off switch.</summary>
public class ToggleSwitchPeer : UIElementPeer
{
    private readonly ToggleSwitch _switch;

    public ToggleSwitchPeer(ToggleSwitch owner) : base(owner)
    {
        _switch = owner;
        owner.Toggled += (_, _) => RaisePropertyChanged(AccessibilityProperty.States);
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.Switch;

    protected override bool NameFromContent => true;

    protected override AccessibilityStates ControlStates =>
        _switch.IsOn ? AccessibilityStates.Checked : AccessibilityStates.None;

    protected override AccessibilityActions ControlActions => AccessibilityActions.Toggle;

    public override bool Toggle() => Owner.PerformAccessibilityActivation();
}
