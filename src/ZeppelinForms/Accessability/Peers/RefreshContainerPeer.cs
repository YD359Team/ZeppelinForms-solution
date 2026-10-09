using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A refresh container: a group that is busy while it refreshes, and
/// refreshes on Invoke — the way to pull for someone who can't drag.</summary>
public class RefreshContainerPeer : UIElementPeer
{
    private readonly RefreshContainer _container;

    public RefreshContainerPeer(RefreshContainer owner) : base(owner)
    {
        _container = owner;

        owner.StateChanged += (_, e) =>
        {
            // only the start and the end of a refresh are news: the pull in between isn't
            if (e.OldState == RefreshState.Refreshing || e.NewState == RefreshState.Refreshing)
                RaisePropertyChanged(AccessibilityProperty.States);
        };
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.Group;

    protected override AccessibilityStates ControlStates =>
        _container.IsRefreshing ? AccessibilityStates.Busy : AccessibilityStates.None;

    protected override AccessibilityActions ControlActions =>
        _container.IsRefreshing || !_container.IsEffectivelyEnabled
            ? AccessibilityActions.None
            : AccessibilityActions.Invoke;

    public override bool Invoke()
    {
        if (_container.IsRefreshing || !_container.IsEffectivelyEnabled) return false;

        _container.RequestRefresh();
        return true;
    }
}