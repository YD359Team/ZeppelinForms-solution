using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Animation;

/// <summary>A running transition: what is visible while the property travels to its target.</summary>
internal interface IPropertyTransition
{
    StyledProperty Property { get; }
}

/// <summary>
/// A transition of one property of one element. Lives on the form's shared
/// clock like any other animation: suspension in an invisible subtree,
/// stopping frames and displacement by key come for free.
/// </summary>
internal sealed class PropertyTransition<T> : IAnimation, IPropertyTransition
{
    private readonly UIElement _element;
    private readonly StyledProperty<T> _property;
    private readonly Func<T, T, float, T> _interpolate;
    private readonly Func<float, float> _easing;
    private readonly TimeSpan _duration;
    private readonly T _from;

    private TimeSpan _elapsed;

    public PropertyTransition(
        UIElement element,
        StyledProperty<T> property,
        T from,
        Transition rule,
        Func<T, T, float, T> interpolate)
    {
        _element = element;
        _property = property;
        _from = from;
        _duration = rule.Duration;
        _easing = rule.Easing;
        _interpolate = interpolate;

        Current = from;
    }

    /// <summary>The value that drawing sees.</summary>
    public T Current { get; private set; }

    public StyledProperty Property => _property;

    public object Target => _element;

    /// <summary>One transition per property: a second assignment in a row
    /// displaces the first rather than running alongside it.</summary>
    public string Key => $"transition:{_property.Index}";

    public bool Advance(TimeSpan elapsed)
    {
        _elapsed += elapsed;

        float t = _duration <= TimeSpan.Zero
            ? 1f
            : Math.Clamp((float)(_elapsed / _duration), 0f, 1f);

        // the target is read every frame rather than remembered at the start:
        // a theme or a binding may rewrite it on the way, and there is no point
        // continuing toward a value that has been cancelled
        T target = _property.GetValue(_element);

        Current = t >= 1f ? target : _interpolate(_from, target, _easing(t));

        _element.InvalidateTransitionVisual();

        if (t < 1f) return true;

        _element.RemoveTransition(this);
        return false;
    }

    public void Cancel(bool applyFinalValue)
    {
        // there is nowhere to bring the value: the target already lives
        // in the property itself, and as soon as the transition is removed,
        // drawing reads it
        _element.RemoveTransition(this);
    }
}