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

            // без формы анимировать некому — просто ставим конечное значение
            if (owner is null) { apply(to); return; }

            owner.AddAnimation(new Animation<T>(element, key, from, to, duration, interpolate, apply, easing, completed));
        }

        /// <summary>Бесконечная анимация. Без формы просто не запускается:
        /// показывать нечего и тикать некому.</summary>
        public void AnimateLoop(string key, TimeSpan period, Action<float> apply) =>
            element.FindOwner()?.AddAnimation(new LoopAnimation(element, key, period, apply));

        public void StopAnimation(string key) =>
            element.FindOwner()?.RemoveAnimation(element, key);
    }
}