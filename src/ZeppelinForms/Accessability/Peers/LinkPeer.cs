using ZeppelinForms.Forms.Controls.Text;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A link: named by its text, followed by Invoke; the address is its value.</summary>
public class LinkPeer : UIElementPeer
{
    private readonly LinkLabel _link;

    public LinkPeer(LinkLabel owner) : base(owner) => _link = owner;

    protected override AccessibilityRole DefaultRole => AccessibilityRole.Link;

    protected override bool NameFromContent => true;

    public override string? Value => _link.Url;

    protected override AccessibilityStates ControlStates =>
        _link.IsVisited ? AccessibilityStates.Visited : AccessibilityStates.None;

    protected override AccessibilityActions ControlActions => AccessibilityActions.Invoke;

    public override bool Invoke() => Owner.PerformAccessibilityActivation();
}