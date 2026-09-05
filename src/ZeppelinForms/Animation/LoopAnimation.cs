namespace ZeppelinForms.Animation;

/// <summary>
/// Бесконечный цикл: отдаёт фазу от 0 до 1 и начинает заново.
/// Останавливается только снятием — сама не заканчивается никогда.
/// </summary>
public sealed class LoopAnimation : IAnimation
{
    private readonly Action<float> _apply;
    private readonly TimeSpan _period;

    private TimeSpan _elapsed;

    public object Target { get; }
    public string Key { get; }

    public LoopAnimation(object target, string key, TimeSpan period, Action<float> apply)
    {
        Target = target;
        Key = key;
        _period = period > TimeSpan.Zero ? period : TimeSpan.FromSeconds(1);
        _apply = apply;
    }

    public bool Advance(TimeSpan elapsed)
    {
        _elapsed += elapsed;

        // вычитаем в цикле, а не по модулю: при подвисании на несколько
        // периодов фаза всё равно останется в пределах одного оборота
        while (_elapsed >= _period)
            _elapsed -= _period;

        _apply((float)(_elapsed / _period));

        return true;
    }
}
