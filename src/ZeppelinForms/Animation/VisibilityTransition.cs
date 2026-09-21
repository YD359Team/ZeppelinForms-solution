using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Animation;

/// <summary>
/// Как элемент появляется и исчезает: из какого вида он приходит
/// и в какой уходит. Вид — прозрачность, масштаб и сдвиг от своего места.
/// </summary>
/// <remarks>
/// Одно описание годится для обеих сторон: появление едет из заданного
/// вида к обычному, исчезание — от обычного к тому же виду. Так строка,
/// выехавшая снизу, и уезжает вниз, а не куда-то ещё.
///
/// Всё это — свойства отрисовки: появление не двигает соседей, а уходящий
/// элемент к моменту начала анимации уже убран из раскладки. Соседи
/// сразу занимают его место, и если у панели задан LayoutTransition,
/// то плавно въезжают в образовавшийся просвет.
/// </remarks>
public sealed class VisibilityTransition
{
    private VisibilityTransition(
        TimeSpan duration,
        Func<float, float> easing,
        float opacity,
        float scale,
        float offsetX,
        float offsetY)
    {
        Duration = duration;
        Easing = easing;
        Opacity = opacity;
        Scale = scale;
        OffsetX = offsetX;
        OffsetY = offsetY;

        ForOpacity = Transition.Ease(UIElement.OpacityProperty, duration, easing);
        ForScaleX = Transition.Ease(UIElement.ScaleXProperty, duration, easing);
        ForScaleY = Transition.Ease(UIElement.ScaleYProperty, duration, easing);
        ForTranslateX = Transition.Ease(UIElement.TranslateXProperty, duration, easing);
        ForTranslateY = Transition.Ease(UIElement.TranslateYProperty, duration, easing);
    }

    public TimeSpan Duration { get; }

    public Func<float, float> Easing { get; }

    /// <summary>Прозрачность в невидимом состоянии. 0 — полностью прозрачен.</summary>
    public float Opacity { get; }

    /// <summary>Масштаб в невидимом состоянии. 1 — без масштабирования.</summary>
    public float Scale { get; }

    /// <summary>Сдвиг от своего места в невидимом состоянии.</summary>
    public float OffsetX { get; }

    public float OffsetY { get; }

    // правила для появления готовятся один раз: оно случается на каждой
    // вставке строки, и плодить по пять объектов на строку незачем
    internal Transition ForOpacity { get; }
    internal Transition ForScaleX { get; }
    internal Transition ForScaleY { get; }
    internal Transition ForTranslateX { get; }
    internal Transition ForTranslateY { get; }

    /// <summary>Проявление из прозрачности.</summary>
    public static VisibilityTransition Fade(int durationMs, Func<float, float>? easing = null) =>
        new(TimeSpan.FromMilliseconds(durationMs), easing ?? Animation.Easing.EaseOut, 0f, 1f, 0f, 0f);

    /// <summary>Проявление с лёгким увеличением — для карточек и всплывающего.</summary>
    public static VisibilityTransition FadeScale(
        int durationMs,
        float scale = 0.92f,
        Func<float, float>? easing = null) =>
        new(TimeSpan.FromMilliseconds(durationMs), easing ?? Animation.Easing.EaseOut, 0f, scale, 0f, 0f);

    /// <summary>Проявление со сдвигом — для строк списков и уведомлений.</summary>
    public static VisibilityTransition Slide(
        int durationMs,
        float offsetX,
        float offsetY,
        Func<float, float>? easing = null) =>
        new(TimeSpan.FromMilliseconds(durationMs), easing ?? Animation.Easing.EaseOut, 0f, 1f, offsetX, offsetY);
}