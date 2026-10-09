using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A split view: a group that expands and collapses its pane. Its
/// children are the pane — while it shows at all — and the content.</summary>
public class SplitViewPeer : UIElementPeer
{
    private readonly SplitView _view;

    public SplitViewPeer(SplitView owner) : base(owner)
    {
        _view = owner;

        owner.IsPaneOpenChanged += (_, _) =>
        {
            RaisePropertyChanged(AccessibilityProperty.States);

            // a pane closed to nothing leaves the tree, an opening one comes back
            RaiseStructureChanged();
        };
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.Group;

    protected override AccessibilityStates ControlStates =>
        _view.IsPaneOpen ? AccessibilityStates.Expanded : AccessibilityStates.Collapsed;

    protected override AccessibilityActions ControlActions =>
        _view.IsPaneOpen ? AccessibilityActions.Collapse : AccessibilityActions.Expand;

    public override bool Expand()
    {
        if (_view.IsPaneOpen || !_view.IsEffectivelyEnabled) return false;

        _view.IsPaneOpen = true;
        return _view.IsPaneOpen;
    }

    public override bool Collapse()
    {
        if (!_view.IsPaneOpen || !_view.IsEffectivelyEnabled) return false;

        // PaneClosing may keep it open
        _view.IsPaneOpen = false;
        return !_view.IsPaneOpen;
    }
}

/// <summary>The pane of a split view: a group named by the view's pane title.</summary>
public class SplitViewPanePeer : UIElementPeer
{
    private readonly SplitView _view;

    internal SplitViewPanePeer(SplitViewPaneHost owner) : base(owner)
    {
        _view = owner.View;
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.Group;

    protected override bool NameFromContent => true;

    /// <summary>The title, not the texts inside: those are the pane's children.</summary>
    protected override string? ContentName => _view.PaneTitle ?? Localization.Get(ZfText.Pane);
}