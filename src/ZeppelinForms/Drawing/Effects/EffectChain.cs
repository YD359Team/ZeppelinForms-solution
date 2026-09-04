using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing.Effects;

/// <summary>Набор эффектов элемента. Применяются в порядке добавления.</summary>
public sealed class EffectChain
{
    private readonly List<VisualEffect> _effects = [];

    public IReadOnlyList<VisualEffect> Effects => _effects;

    public bool IsEmpty => _effects.Count == 0;

    public float TotalBleed
    {
        get
        {
            float bleed = 0;

            foreach (VisualEffect effect in _effects)
                bleed = Math.Max(bleed, effect.BleedRadius);

            return bleed;
        }
    }

    public event EventHandler? Changed;

    public EffectChain Add(VisualEffect effect)
    {
        _effects.Add(effect);
        Changed?.Invoke(this, EventArgs.Empty);
        return this;
    }

    public bool Remove(VisualEffect effect)
    {
        bool removed = _effects.Remove(effect);

        if (removed) Changed?.Invoke(this, EventArgs.Empty);

        return removed;
    }

    public T? Get<T>() where T : VisualEffect
    {
        foreach (VisualEffect effect in _effects)
            if (effect is T typed)
                return typed;

        return null;
    }

    public void Clear()
    {
        if (_effects.Count == 0) return;

        _effects.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Begin(Graphics g, Rectangle bounds)
    {
        foreach (VisualEffect effect in _effects)
            effect.Begin(g, bounds);
    }

    internal void End(Graphics g, Rectangle bounds)
    {
        // в обратном порядке: слои закрываются как скобки
        for (int i = _effects.Count - 1; i >= 0; i--)
            _effects[i].End(g, bounds);
    }
}
