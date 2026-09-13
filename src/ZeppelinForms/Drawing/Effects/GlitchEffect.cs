using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Effects;
using ZeppelinForms.Drawing.Primitives;

/// <summary>
/// Сбой сигнала: разделение цветовых каналов и горизонтальные срезы,
/// съехавшие вбок.
/// </summary>
/// <remarks>
/// Элемент рисуется в захват и на экран не попадает напрямую — вместо него
/// выводятся копии. Поэтому канвас без поддержки захвата обрабатывается
/// отдельно: иначе элемент просто исчез бы.
///
/// Случайность выводится из Seed и шага фазы, а не из Random: снимковые
/// тесты обязаны получать одну и ту же картинку при одной фазе.
/// </remarks>
public sealed class GlitchEffect : VisualEffect
{
    /// <summary>Общая сила, 0 — эффекта нет.</summary>
    public float Intensity { get; set; } = 1f;

    /// <summary>На сколько пикселей расходятся красный и голубой каналы.</summary>
    public float ChromaticOffset { get; set; } = 3f;

    /// <summary>Предельный сдвиг среза вбок.</summary>
    public float SliceDisplacement { get; set; } = 12f;

    /// <summary>На сколько полос режется элемент.</summary>
    public int SliceCount { get; set; } = 8;

    /// <summary>Доля полос, которые съезжают на текущем шаге.</summary>
    public float SliceChance { get; set; } = 0.35f;

    public int Seed { get; set; } = 1;

    /// <summary>Шаг анимации. Меняется скачками, а не плавно: глитч
    /// дёргается, а не переползает.</summary>
    public int Step { get; set; }

    private bool _capturing;

    public override Thickness Bleed(Rectangle bounds)
    {
        float x = (ChromaticOffset + SliceDisplacement) * MathF.Max(0f, Intensity);
        return new Thickness(x, 0f, x, 0f);
    }

    public override void Begin(Graphics g, Rectangle bounds)
    {
        _capturing = g.SupportsLayerCapture && Intensity > 0f;

        if (_capturing) g.BeginCapture(bounds);
    }

    public override void End(Graphics g, Rectangle bounds)
    {
        if (!_capturing) return;

        using LayerCapture? capture = g.EndCapture();

        // захват не получился — элемент уже нарисован в никуда,
        // и вернуть его неоткуда; хотя бы не падаем
        if (capture is null) return;

        float intensity = Math.Clamp(Intensity, 0f, 1f);

        // основа: целый элемент на своём месте
        g.DrawCapture(capture, bounds);

        // расхождение каналов — обе половины поверх основы с осветлением
        float offset = ChromaticOffset * intensity;

        if (offset > 0f)
        {
            g.DrawCapture(capture, Shift(bounds, -offset),
                channels: ColorChannels.Red, blend: CaptureBlend.Screen);

            g.DrawCapture(capture, Shift(bounds, offset),
                channels: ColorChannels.Cyan, blend: CaptureBlend.Screen);
        }

        // срезы: часть полос перерисовывается со сдвигом поверх основы
        if (SliceCount <= 0 || SliceDisplacement <= 0f) return;

        float sliceHeight = bounds.Height / SliceCount;

        for (int i = 0; i < SliceCount; i++)
        {
            uint noise = Hash((uint)Seed, (uint)Step, (uint)i);

            // съезжает не каждая полоса — сплошной сдвиг выглядит как рябь,
            // а не как сбой
            if (Unit(noise) > SliceChance) continue;

            float shift = (Unit(noise >> 8) * 2f - 1f) * SliceDisplacement * intensity;

            var band = new Rectangle(
                new Point(bounds.X, bounds.Y + i * sliceHeight),
                new Size(bounds.Width, sliceHeight));

            g.DrawCapture(capture, Shift(band, shift), sourceClip: band);
        }
    }

    private static Rectangle Shift(Rectangle rect, float dx) =>
        new(new Point(rect.X + dx, rect.Y), new Size(rect.Width, rect.Height));

    /// <summary>Свой хэш вместо Random: результат обязан совпадать между
    /// платформами и прогонами, иначе снимки станут недетерминированными.</summary>
    private static uint Hash(uint a, uint b, uint c)
    {
        uint h = a * 374761393u + b * 668265263u + c * 2246822519u;

        h ^= h >> 13;
        h *= 1274126177u;
        h ^= h >> 16;

        return h;
    }

    private static float Unit(uint value) => (value & 0xFFFFFF) / (float)0x1000000;
}
