using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing.Effects;

/// <summary>Произвольное аффинное преобразование: сдвиг, масштаб, наклон, поворот.</summary>
public sealed class TransformEffect : VisualEffect
{
    public float TranslateX { get; set; }
    public float TranslateY { get; set; }
    public float ScaleX { get; set; } = 1f;
    public float ScaleY { get; set; } = 1f;
    public float SkewX { get; set; }
    public float SkewY { get; set; }
    public float Rotation { get; set; }

    /// <summary>Точка, вокруг которой всё происходит, в долях от размера.</summary>
    public Point Origin { get; set; } = new(0.5f, 0.5f);

    /// <summary>Описанный прямоугольник вокруг преобразованного:
    /// считаем по четырём углам, точная форма тут не нужна.</summary>
    public override Thickness Bleed(Rectangle bounds)
    {
        float scaleW = bounds.Width * MathF.Max(1f, MathF.Abs(ScaleX)) - bounds.Width;
        float scaleH = bounds.Height * MathF.Max(1f, MathF.Abs(ScaleY)) - bounds.Height;

        // поворот и наклон в худшем случае уводят угол на половину диагонали
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

        // все преобразования вокруг заданной точки: сдвигаем начало
        // координат туда, работаем, возвращаем обратно
        g.Translate(cx + TranslateX, cy + TranslateY);

        if (Rotation != 0f) g.Rotate(Rotation);
        if (SkewX != 0f || SkewY != 0f) g.Skew(SkewX, SkewY);
        if (ScaleX != 1f || ScaleY != 1f) g.Scale(ScaleX, ScaleY);

        g.Translate(-cx, -cy);
    }

    public override void End(Graphics g, Rectangle bounds) => g.Restore();
}