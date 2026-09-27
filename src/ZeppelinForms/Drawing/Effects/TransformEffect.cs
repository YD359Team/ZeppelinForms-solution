using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing.Effects;

/// <summary>An arbitrary affine transform: translation, scale, skew, rotation.</summary>
public sealed class TransformEffect : VisualEffect
{
    public float TranslateX { get; set; }
    public float TranslateY { get; set; }
    public float ScaleX { get; set; } = 1f;
    public float ScaleY { get; set; } = 1f;
    public float SkewX { get; set; }
    public float SkewY { get; set; }
    public float Rotation { get; set; }

    /// <summary>The point everything happens around, as fractions of the size.</summary>
    public Point Origin { get; set; } = new(0.5f, 0.5f);

    /// <summary>The bounding rectangle around the transformed one: computed from
    /// the four corners, the exact shape is not needed here.</summary>
    public override Thickness Bleed(Rectangle bounds)
    {
        float scaleW = bounds.Width * MathF.Max(1f, MathF.Abs(ScaleX)) - bounds.Width;
        float scaleH = bounds.Height * MathF.Max(1f, MathF.Abs(ScaleY)) - bounds.Height;

        // rotation and skew in the worst case take a corner away by half the diagonal
        float spin = Rotation != 0f || SkewX != 0f || SkewY != 0f
            ? MathF.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height) / 2f
            : 0f;

        float x = MathF.Abs(TranslateX) + scaleW / 2f + spin;
        float y = MathF.Abs(TranslateY) + scaleH / 2f + spin;

        return new Thickness(x, y, x, y);
    }

    public override void Begin(Graphics g, Rectangle bounds)
    {
        float cx = bounds.X + bounds.Width * Origin.X;
        float cy = bounds.Y + bounds.Height * Origin.Y;

        g.Save();

        // all transforms around the given point: move the origin there,
        // do the work, move it back
        g.Translate(cx + TranslateX, cy + TranslateY);

        if (Rotation != 0f) g.Rotate(Rotation);
        if (SkewX != 0f || SkewY != 0f) g.Skew(SkewX, SkewY);
        if (ScaleX != 1f || ScaleY != 1f) g.Scale(ScaleX, ScaleY);

        g.Translate(-cx, -cy);
    }

    public override void End(Graphics g, Rectangle bounds) => g.Restore();
}