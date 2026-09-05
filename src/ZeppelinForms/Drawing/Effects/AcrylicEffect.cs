using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing.Effects;

/// <summary>
/// Матовое стекло: размытая подложка плюс полупрозрачный тон и шум.
/// Читает уже нарисованное под элементом, поэтому требует слоя.
/// </summary>
public sealed class AcrylicEffect : VisualEffect
{
    public float BlurRadius { get; set; } = 20f;
    public Color TintColor { get; set; } = new Color(140, 255, 255, 255);
    public float NoiseOpacity { get; set; } = 0.03f;

    public override void Begin(Graphics g, Rectangle bounds)
    {
        // подложка размывается до отрисовки элемента: сам элемент
        // должен лечь поверх матового стекла, а не под него
        g.BlurBackdrop(bounds, BlurRadius);

        if (TintColor.A > 0)
            g.FillRectangle(bounds, TintColor);

        if (NoiseOpacity > 0)
            g.FillNoise(bounds, NoiseOpacity);
    }
}