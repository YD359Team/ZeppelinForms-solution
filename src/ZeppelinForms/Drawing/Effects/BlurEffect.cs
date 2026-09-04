using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing.Effects;

public sealed class BlurEffect(float radius) : VisualEffect
{
    public float Radius { get; set; } = radius;

    public override float BleedRadius => Radius * 1.5f;
    public override bool RequiresLayer => true;

    public override void Begin(Graphics g, Rectangle bounds) => g.SaveBlurLayer(Radius);

    public override void End(Graphics g, Rectangle bounds) => g.Restore();
}