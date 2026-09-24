using System.Runtime.CompilerServices;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Animation;

/// <summary>
/// An exiting element: already removed from the panel, but still drawn
/// until its disappearance finishes playing.
/// </summary>
/// <remarks>
/// The animation hangs on the panel rather than on the element itself:
/// by this point the element is detached from the form, and the clock
/// no longer sees it. For the same reason the values here do not go
/// through the property system but are computed from the progress right
/// at draw time — a detached element has neither an owner nor frames.
/// </remarks>
internal sealed class ExitingChild : IAnimation
{
    private readonly PanelControl _panel;
    private TimeSpan _elapsed;

    public ExitingChild(PanelControl panel, UIElement element, VisibilityTransition rule)
    {
        _panel = panel;
        Element = element;
        Rule = rule;
        Key = $"exit:{RuntimeHelpers.GetHashCode(element)}";
    }

    public UIElement Element { get; }

    public VisibilityTransition Rule { get; }

    /// <summary>The eased fraction of the exit that has elapsed: 0 — the element
    /// looks as usual, 1 — it is completely gone.</summary>
    public float Progress { get; private set; }

    public object Target => _panel;

    public string Key { get; }

    public bool Advance(TimeSpan elapsed)
    {
        _elapsed += elapsed;

        float t = Rule.Duration <= TimeSpan.Zero
            ? 1f
            : Math.Clamp((float)(_elapsed / Rule.Duration), 0f, 1f);

        Progress = Rule.Easing(t);

        if (t < 1f) return true;

        _panel.RemoveExiting(this);
        return false;
    }

    public void Cancel(bool applyFinalValue) => _panel.RemoveExiting(this);
}