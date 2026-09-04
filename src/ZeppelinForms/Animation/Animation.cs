namespace ZeppelinForms.Animation;

public sealed class Animation<T> : IAnimation
{
    private readonly Func<T, T, float, T> _interpolate;
    private readonly Action<T> _apply;
    private readonly T _from;
    private readonly T _to;
    private readonly TimeSpan _duration;
    private readonly Func<float, float> _easing;
    private readonly Action? _completed;

    private TimeSpan _elapsed;

    public object Target { get; }

    /// <summary>Что именно анимируется. Новая анимация с тем же ключом
    /// вытесняет предыдущую — иначе два наведения подряд подерутся за цвет.</summary>
    public string Key { get; }

    public Animation(
        object target, string key,
        T from, T to, TimeSpan duration,
        Func<T, T, float, T> interpolate,
        Action<T> apply,
        Func<float, float>? easing = null,
        Action? completed = null)
    {
        Target = target;
        Key = key;
        _from = from;
        _to = to;
        _duration = duration;
        _interpolate = interpolate;
        _apply = apply;
        _easing = easing ?? Easing.EaseOut;
        _completed = completed;
    }

    public bool Advance(TimeSpan elapsed)
    {
        _elapsed += elapsed;

        float t = _duration <= TimeSpan.Zero
            ? 1f
            : Math.Clamp((float)(_elapsed / _duration), 0f, 1f);

        _apply(_interpolate(_from, _to, _easing(t)));

        if (t < 1f) return true;

        _completed?.Invoke();
        return false;
    }
}
