using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Animation;

/// <summary>Идущий переход: то, что видно, пока свойство едет к цели.</summary>
internal interface IPropertyTransition
{
    StyledProperty Property { get; }
}

/// <summary>
/// Переход одного свойства одного элемента. Живёт на общих часах формы,
/// как и любая другая анимация: гашение на невидимом поддереве, остановка
/// кадров и вытеснение по ключу достаются даром.
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

    /// <summary>Значение, которое видит отрисовка.</summary>
    public T Current { get; private set; }

    public StyledProperty Property => _property;

    public object Target => _element;

    /// <summary>Один переход на свойство: второе присваивание подряд
    /// вытесняет первое, а не едет рядом с ним.</summary>
    public string Key => $"transition:{_property.Index}";

    public bool Advance(TimeSpan elapsed)
    {
        _elapsed += elapsed;

        float t = _duration <= TimeSpan.Zero
            ? 1f
            : Math.Clamp((float)(_elapsed / _duration), 0f, 1f);

        // цель читается каждый кадр, а не запоминается на старте: тема
        // или биндинг могут переписать её по дороге, и ехать после этого
        // к отменённому значению незачем
        T target = _property.GetValue(_element);

        Current = t >= 1f ? target : _interpolate(_from, target, _easing(t));

        _element.InvalidateTransitionVisual();

        if (t < 1f) return true;

        _element.RemoveTransition(this);
        return false;
    }

    public void Cancel(bool applyFinalValue)
    {
        // значение доводить некуда: цель уже лежит в самом свойстве,
        // и как только переход снят, отрисовка читает её
        _element.RemoveTransition(this);
    }
}