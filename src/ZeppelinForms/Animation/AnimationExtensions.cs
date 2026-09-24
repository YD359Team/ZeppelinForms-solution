using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Animation;

public static class AnimationExtensions
{
    extension(UIElement element)
    {
        public void Animate<T>(
             string key, T from, T to, TimeSpan duration,
             Func<T, T, float, T> interpolate,
             Action<T> apply,
             Func<float, float>? easing = null,
             Action? completed = null)
        {
            Form? owner = element.FindOwner();

            // without a form there is nobody to animate — just set the final value
            if (owner is null) { apply(to); return; }

            owner.AddAnimation(new Animation<T>(element, key, from, to, duration, interpolate, apply, easing, completed));
        }

        /// <summary>An endless animation. Without a form it simply does not start:
        /// there is nothing to show and nobody to tick it.</summary>
        public void AnimateLoop(string key, TimeSpan period, Action<float> apply) =>
            element.FindOwner()?.AddAnimation(new LoopAnimation(element, key, period, apply));

        public void StopAnimation(string key) =>
            element.FindOwner()?.RemoveAnimation(element, key);
    }
}