using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A button that stays pressed: Toggle switches it.</summary>
public class ToggleButtonPeer : UIElementPeer
{
    private readonly ToggleButton _button;

    public ToggleButtonPeer(ToggleButton owner) : base(owner)
    {
        _button = owner;
        owner.CheckedChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.States);
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.ToggleButton;

    protected override bool NameFromContent => true;

    protected override AccessibilityStates ControlStates =>
        _button.IsChecked ? AccessibilityStates.Checked : AccessibilityStates.None;

    protected override AccessibilityActions ControlActions => AccessibilityActions.Toggle;

    public override bool Toggle() => Owner.PerformAccessibilityActivation();
}
