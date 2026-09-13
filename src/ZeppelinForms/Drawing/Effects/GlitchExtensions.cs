using ZeppelinForms.Animation;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;

public static class GlitchExtensions
{
    /// <summary>Запустить непрерывный глитч.</summary>
    /// <remarks>
    /// Целью анимации выступает сам элемент, а не эффект: Form.Tick
    /// перерисовывает цель по типу, и эффект, не будучи UIElement,
    /// перерисовки бы не вызвал.
    ///
    /// Фаза переводится в шаг скачком: глитч дёргается, а не переползает,
    /// и промежуточные значения фазы внутри шага дают ту же картинку.
    /// </remarks>
    public static GlitchEffect Glitch(
        this UIElement element,
        float intensity = 1f,
        int stepsPerSecond = 12)
    {
        var effect = new GlitchEffect { Intensity = intensity };
        element.Effects.Add(effect);

        Form? form = element.FindOwner();

        // элемент ещё не в дереве — эффект добавлен, крутить его пока нечем.
        // Вызвать Glitch заново после присоединения дешевле, чем подписываться
        // на Attached и держать ссылку
        if (form is null) return effect;

        int steps = Math.Max(1, stepsPerSecond);

        var animation = new LoopAnimation(
            element, "glitch", TimeSpan.FromSeconds(1),
            phase => effect.Step = (int)(phase * steps));

        form.AddAnimation(animation);

        return effect;
    }

    /// <summary>Убрать глитч и остановить его анимацию.</summary>
    public static void StopGlitch(this UIElement element)
    {
        if (element.Effects.Get<GlitchEffect>() is not { } effect) return;

        element.FindOwner()?.RemoveAnimation(element, "glitch");
        element.Effects.Remove(effect);
    }
}