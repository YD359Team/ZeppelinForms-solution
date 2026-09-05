using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing.Effects;

/// <summary>Отражение под элементом с затуханием.</summary>
public sealed class ReflectionEffect : VisualEffect
{
    /// <summary>Высота отражения в долях от высоты элемента.</summary>
    public float Height { get; set; } = 0.4f;

    public float Gap { get; set; } = 2f;
    public float StartOpacity { get; set; } = 0.35f;

    public override float BleedRadius => 0f;

    public override void Begin(Graphics g, Rectangle bounds) { }

    public override void End(Graphics g, Rectangle bounds)
    {
        // отражение строится по уже отрисованному элементу,
        // поэтому только в End
        g.DrawReflection(bounds, Height, Gap, StartOpacity);
    }
}