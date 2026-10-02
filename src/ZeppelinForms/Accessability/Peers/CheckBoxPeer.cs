using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A check box, with its third state.</summary>
public class CheckBoxPeer : UIElementPeer
{
    private readonly CheckBox _box;

    public CheckBoxPeer(CheckBox owner) : base(owner)
    {
        _box = owner;
        owner.CheckedChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.States);
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.CheckBox;

    protected override bool NameFromContent => true;

    protected override AccessibilityStates ControlStates => _box.CheckedState switch
    {
        CheckedState.Checked => AccessibilityStates.Checked,
        CheckedState.Intermediate => AccessibilityStates.Mixed,
        _ => AccessibilityStates.None,
    };

    protected override AccessibilityActions ControlActions => AccessibilityActions.Toggle;

    public override bool Toggle() => Owner.PerformAccessibilityActivation();
}
