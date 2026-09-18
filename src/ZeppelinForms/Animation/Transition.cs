using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Animation;

/// <summary>
/// Правило: как свойство добирается до нового значения. Присваивание
/// становится целью, а видимое значение идёт к ней за время перехода.
/// </summary>
/// <remarks>
/// Само присваивание при этом мгновенно: код, биндинги и PropertyGrid
/// читают цель, а не полпути. Промежуточное значение живёт отдельным
/// слоем, который читает отрисовка. Так устроены Core Animation, CSS
/// и Compose, и именно это разделение позволяет когда-нибудь увезти
/// переходы на поток отрисовки, не трогая модель.
/// </remarks>
public sealed class Transition
{
    private Transition(StyledProperty property, TimeSpan duration, Func<float, float> easing)
    {
        Property = property;
        Duration = duration;
        Easing = easing;
    }

    public StyledProperty Property { get; }

    public TimeSpan Duration { get; }

    public Func<float, float> Easing { get; }

    /// <summary>Переход с длительностью и кривой.</summary>
    public static Transition Ease(
        StyledProperty property,
        TimeSpan duration,
        Func<float, float>? easing = null) =>
        new(property, duration, easing ?? Animation.Easing.EaseOut);

    /// <summary>То же в миллисекундах — так короче в разметке контрола.</summary>
    public static Transition Ease(
        StyledProperty property,
        int durationMs,
        Func<float, float>? easing = null) =>
        Ease(property, TimeSpan.FromMilliseconds(durationMs), easing);
}