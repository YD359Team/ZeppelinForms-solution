using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A button with a menu: pressing runs its action, expanding opens the menu.</summary>
public class SplitButtonPeer : ButtonPeer
{
    private readonly SplitButton _button;

    public SplitButtonPeer(SplitButton owner) : base(owner) => _button = owner;

    protected override AccessibilityRole DefaultRole => AccessibilityRole.SplitButton;

    protected override AccessibilityStates ControlStates =>
        _button.IsMenuOpen ? AccessibilityStates.Expanded : AccessibilityStates.Collapsed;
}
