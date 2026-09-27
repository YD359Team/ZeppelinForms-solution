using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing.Effects;

/// <summary>
/// A signal failure: color channel separation and horizontal slices
/// shifted sideways.
/// </summary>
/// <remarks>
/// The element is drawn into a capture and doesn't get onto the screen directly —
/// copies are drawn instead. So a canvas without capture support is handled
/// separately: otherwise the element would simply disappear.
///
/// The randomness is derived from Seed and the phase step rather than from Random:
/// snapshot tests must get the same picture for the same phase.
/// </remarks>
public sealed class GlitchEffect : VisualEffect
{
    /// <summary>The overall strength, 0 — no effect.</summary>
    public float Intensity { get; set; } = 1f;

    /// <summary>How many pixels the red and cyan channels move apart.</summary>
    public float ChromaticOffset { get; set; } = 3f;

    /// <summary>The maximum sideways shift of a slice.</summary>
    public float SliceDisplacement { get; set; } = 12f;

    /// <summary>How many bands the element is cut into.</summary>
    public int SliceCount { get; set; } = 8;

    /// <summary>The fraction of bands that shift on the current step.</summary>
    public float SliceChance { get; set; } = 0.35f;

    public int Seed { get; set; } = 1;

    /// <summary>The animation step. Changes in jumps rather than smoothly:
    /// a glitch twitches rather than crawls.</summary>
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

        // the capture failed — the element has already been drawn into nowhere,
        // and there is nowhere to get it back from; at least we don't crash
        if (capture is null) return;

        float intensity = Math.Clamp(Intensity, 0f, 1f);

        // the base: the whole element in its place
        g.DrawCapture(capture, bounds);

        // channel separation — both halves on top of the base, blended with screen
        float offset = ChromaticOffset * intensity;

        if (offset > 0f)
        {
            g.DrawCapture(capture, Shift(bounds, -offset),
                channels: ColorChannels.Red, blend: CaptureBlend.Screen);

            g.DrawCapture(capture, Shift(bounds, offset),
                channels: ColorChannels.Cyan, blend: CaptureBlend.Screen);
        }

        // slices: some of the bands are redrawn with a shift on top of the base
        if (SliceCount <= 0 || SliceDisplacement <= 0f) return;

        float sliceHeight = bounds.Height / SliceCount;

        for (int i = 0; i < SliceCount; i++)
        {
            uint noise = Hash((uint)Seed, (uint)Step, (uint)i);

            // not every band shifts — a solid shift looks like ripples,
            // not like a failure
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

    /// <summary>An own hash instead of Random: the result must match across
    /// platforms and runs, otherwise snapshots would become non-deterministic.</summary>
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