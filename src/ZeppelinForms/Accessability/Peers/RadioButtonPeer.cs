using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A radio button: selecting it unchecks the others of its group.</summary>
public class RadioButtonPeer : UIElementPeer
{
    private readonly RadioButton _radio;

    public RadioButtonPeer(RadioButton owner) : base(owner)
    {
        _radio = owner;
        owner.CheckedChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.States);
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.RadioButton;

    protected override bool NameFromContent => true;

    protected override AccessibilityStates ControlStates =>
        AccessibilityStates.Selectable |
        (_radio.IsChecked ? AccessibilityStates.Checked | AccessibilityStates.Selected : 0);

    protected override AccessibilityActions ControlActions => AccessibilityActions.Select;

    public override bool Select() => _radio.IsChecked || Owner.PerformAccessibilityActivation();

    /// <summary>"2 of 3" within its group: the siblings with the same group name.</summary>
    public override SetPosition? Position
    {
        get
        {
            if (Owner.Parent is not PanelControl panel) return null;

            List<RadioButton> group = [.. panel.Children
                .OfType<RadioButton>()
                .Where(radio => radio.IsVisible && radio.GroupName == _radio.GroupName)];

            int index = group.IndexOf(_radio);

            return index < 0 ? null : new SetPosition(index + 1, group.Count);
        }
    }
}
