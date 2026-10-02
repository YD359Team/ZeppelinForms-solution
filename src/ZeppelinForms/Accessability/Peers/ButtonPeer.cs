using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A button: named by its caption, pressed by Invoke.</summary>
public class ButtonPeer(UIElement owner) : UIElementPeer(owner)
{
    protected override AccessibilityRole DefaultRole => AccessibilityRole.Button;

    protected override bool NameFromContent => true;

    protected override AccessibilityActions ControlActions => AccessibilityActions.Invoke;

    public override bool Invoke() => Owner.PerformAccessibilityActivation();
}