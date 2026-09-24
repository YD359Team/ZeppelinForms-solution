using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;

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
    private readonly TimeSpan? _fixedDuration;
    private readonly float _speed;
    private readonly TimeSpan _minDuration;
    private readonly TimeSpan _maxDuration;

    private readonly Transition? _fixedX;
    private readonly Transition? _fixedY;

    private LayoutTransition(TimeSpan duration, Func<float, float> easing)
    {
        _fixedDuration = duration;
        Easing = easing;

        _fixedX = Transition.Ease(UIElement.TranslateXProperty, duration, easing);
        _fixedY = Transition.Ease(UIElement.TranslateYProperty, duration, easing);
    }

    private LayoutTransition(float speed, TimeSpan min, TimeSpan max, Func<float, float> easing)
    {
        _speed = speed;
        _minDuration = min;
        _maxDuration = max;
        Easing = easing;
    }

    public Func<float, float> Easing { get; }

    /// <summary>Постоянная длительность, сколько бы элемент ни ехал.</summary>
    /// <remarks>
    /// Годится, когда расстояния примерно одинаковы. Если они разные,
    /// постоянная длительность врёт: строка, уехавшая на высоту одной
    /// строки, ползёт те же две десятых секунды, что и уехавшая через
    /// весь экран, и выглядит это неестественно медленно.
    /// </remarks>
    public static LayoutTransition Ease(TimeSpan duration, Func<float, float>? easing = null) =>
        new(duration, easing ?? Animation.Easing.EaseOut);

    public static LayoutTransition Ease(int durationMs, Func<float, float>? easing = null) =>
        Ease(TimeSpan.FromMilliseconds(durationMs), easing);

    /// <summary>Длительность по расстоянию: чем дальше ехать, тем дольше.
    /// Границы нужны с обеих сторон — иначе короткий переезд станет
    /// мельканием, а длинный будет тянуться.</summary>
    public static LayoutTransition Speed(
        float pixelsPerSecond = 1600f,
        int minDurationMs = 90,
        int maxDurationMs = 320,
        Func<float, float>? easing = null) =>
        new(
            pixelsPerSecond,
            TimeSpan.FromMilliseconds(minDurationMs),
            TimeSpan.FromMilliseconds(maxDurationMs),
            easing ?? Animation.Easing.EaseOut);

    /// <summary>Правило для одной оси с учётом того, сколько по ней ехать.</summary>
    internal Transition For(StyledProperty<float> property, float distance)
    {
        // постоянную длительность готовим один раз: переезд случается
        // на каждой перестройке списка, и плодить объекты на строку незачем
        if (_fixedDuration is not null)
            return ReferenceEquals(property, UIElement.TranslateXProperty) ? _fixedX! : _fixedY!;

        double milliseconds = MathF.Abs(distance) / _speed * 1000f;

        var duration = TimeSpan.FromMilliseconds(Math.Clamp(
            milliseconds, _minDuration.TotalMilliseconds, _maxDuration.TotalMilliseconds));

        return Transition.Ease(property, duration, Easing);
    }
}