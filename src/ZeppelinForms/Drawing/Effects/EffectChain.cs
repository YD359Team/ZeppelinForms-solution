using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing.Effects;

/// <summary>Набор эффектов элемента. Применяются в порядке добавления.</summary>
public sealed class EffectChain
{
    private readonly List<VisualEffect> _effects = [];

    public IReadOnlyList<VisualEffect> Effects => _effects;

    public bool IsEmpty => _effects.Count == 0;

    /// <summary>Максимальный вылет по каждой стороне среди всех эффектов.</summary>
    public Thickness TotalBleed(Rectangle bounds)
    {
        float left = 0, top = 0, right = 0, bottom = 0;

        foreach (VisualEffect effect in _effects)
        {
            Thickness bleed = effect.Bleed(bounds);

            left = Math.Max(left, bleed.Left);
            top = Math.Max(top, bleed.Top);
            right = Math.Max(right, bleed.Right);
            bottom = Math.Max(bottom, bleed.Bottom);
        }

        return new Thickness(left, top, right, bottom);
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
