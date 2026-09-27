using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing.Effects;

/// <summary>
/// Frosted glass: a blurred backdrop plus a translucent tint and noise.
/// Reads what is already drawn under the element, so it requires a layer.
/// </summary>
public sealed class AcrylicEffect : VisualEffect
{
    public float BlurRadius { get; set; } = 20f;
    public Color TintColor { get; set; } = new Color(140, 255, 255, 255);
    public float NoiseOpacity { get; set; } = 0.03f;

    public override void Begin(Graphics g, Rectangle bounds)
    {
        // the backdrop is blurred before the element is drawn: the element itself
        // must lie on top of the frosted glass, not under it
        g.BlurBackdrop(bounds, BlurRadius);

        if (TintColor.A > 0)
            g.FillRectangle(bounds, TintColor);

        if (NoiseOpacity > 0)
            g.FillNoise(bounds, NoiseOpacity);
    }
}