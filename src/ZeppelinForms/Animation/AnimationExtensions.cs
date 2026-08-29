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
    }
}