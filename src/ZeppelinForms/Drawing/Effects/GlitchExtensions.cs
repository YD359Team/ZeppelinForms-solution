using System.Diagnostics;
using ZeppelinForms.Animation;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;

public static class GlitchExtensions
{
    /// <summary>Убрать глитч и остановить его анимацию.</summary>
    public static void StopGlitch(this UIElement element)
    {
        if (element.Effects.Get<GlitchEffect>() is not { } effect) return;

        element.FindOwner()?.RemoveAnimation(element, "glitch");
        element.Effects.Remove(effect);
    }

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

        Debug.WriteLine("Glitch: вызван");

        int steps = Math.Max(1, stepsPerSecond);

        void Start()
        {
            Form? form = element.FindOwner();
            Debug.WriteLine($"Glitch: Start, форма {(form is null ? "null" : "есть")}");

            if (form is null) return;

            form.AddAnimation(new LoopAnimation(
                element, "glitch", TimeSpan.FromSeconds(1),
                phase =>
                {
                    int step = (int)(phase * steps);
                    if (step == effect.Step) return;

                    Debug.WriteLine($"Glitch: шаг {step}");
                    effect.Step = step;
                    element.InvalidateVisual();
                }));
        }

        if (element.FindOwner() is null)
            element.Attached += (_, _) => Start();
        else
            Start();

        return effect;
    }
}