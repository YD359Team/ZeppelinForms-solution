using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Animation;

/// <summary>
/// Как элемент переезжает, когда раскладка поставила его на новое место.
/// </summary>
/// <remarks>
/// Работает по правилу FLIP: элемент уже стоит там, где решила раскладка,
/// но рисуется от старого места и приезжает к нулевому сдвигу. Двигается
/// при этом не раскладка, а сдвиг при отрисовке — поэтому переезд ничего
/// не стоит соседям, попадание всё время следует за картинкой, а прерванный
/// переезд продолжается с того места, где его застали.
///
/// Отсюда же следует, что контролам ничего не нужно знать про анимацию:
/// раскрытие узла дерева, перестроение WrapPanel при смене ширины окна
/// и вставка в DragList — это всё смена позиции в Arrange, то есть одно
/// и то же событие.
/// </remarks>
public sealed class LayoutTransition
{
    private LayoutTransition(TimeSpan duration, Func<float, float> easing)
    {
        ForTranslateX = Transition.Ease(UIElement.TranslateXProperty, duration, easing);
        ForTranslateY = Transition.Ease(UIElement.TranslateYProperty, duration, easing);
    }

    /// <summary>Правила готовятся один раз: переезд случается на каждой
    /// перестройке списка, и плодить по два объекта на строку незачем.</summary>
    internal Transition ForTranslateX { get; }

    internal Transition ForTranslateY { get; }

    public static LayoutTransition Ease(TimeSpan duration, Func<float, float>? easing = null) =>
        new(duration, easing ?? Animation.Easing.EaseOut);

    public static LayoutTransition Ease(int durationMs, Func<float, float>? easing = null) =>
        Ease(TimeSpan.FromMilliseconds(durationMs), easing);
}