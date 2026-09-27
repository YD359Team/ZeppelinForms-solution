using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing.Effects;

/// <summary>A fading reflection under the element.</summary>
public sealed class ReflectionEffect : VisualEffect
{
    /// <summary>The reflection's height as a fraction of the element's height.</summary>
    public float Height { get; set; } = 0.4f;

    public float Gap { get; set; } = 2f;
    public float StartOpacity { get; set; } = 0.35f;

    // the reflection goes only downward, it doesn't stick out sideways or up
    public override Thickness Bleed(Rectangle bounds) =>
        new(0f, 0f, 0f, bounds.Height * Height + Gap);

    public override void Begin(Graphics g, Rectangle bounds) { }

    public override void End(Graphics g, Rectangle bounds)
    {
        // the reflection is built from the already drawn element,
        // so it happens only in End
        g.DrawReflection(bounds, Height, Gap, StartOpacity);
    }
}