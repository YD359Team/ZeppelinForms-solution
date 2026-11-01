namespace ZeppelinForms.Drawing.Primitives;

/// <summary>
/// Light along an element's border — a neon outline: the line, and its light diffused
/// round it. Static, or a comet of light running along the border.
/// </summary>
/// <param name="Color">The light's color; its alpha is the light's strength.</param>
/// <param name="Blur">How far the light diffuses, as a box shadow's blur: 0 — only the line.</param>
/// <param name="Width">The line's width.</param>
/// <param name="Motion">Whether the light stands or runs.</param>
/// <example>
/// <code>
/// card.Glow = new Glow(Colors.Cyan);
/// button.Glow = new Glow(Colors.HotPink, 10, 2, GlowMotion.Comet) { Period = TimeSpan.FromSeconds(2) };
/// </code>
/// In a style sheet: <c>Glow: #00E5FF 14 2</c>, <c>Glow: #FF2D95 10 2 comet</c>.
/// </example>
public sealed record Glow(Color Color, float Blur = 12f, float Width = 2f, GlowMotion Motion = GlowMotion.Static)
{
    /// <summary>How long the comet takes round the border.</summary>
    public TimeSpan Period { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>The comet's length with its tail, as a share of the border: 0.05 to 1.</summary>
    public float CometLength { get; init; } = 0.3f;

    /// <summary>A cyan tube: the line and a wide light.</summary>
    public static Glow Neon => new(new Color(255, 0x00, 0xE5, 0xFF), 14f, 2f);

    /// <summary>A comet of the accent's blue running round the border.</summary>
    public static Glow Comet => new(new Color(255, 0x3D, 0x8B, 0xFF), 10f, 2.5f, GlowMotion.Comet);

    /// <summary>How far the light reaches past the border: the dirty region and the
    /// shadow's room take it.</summary>
    internal float Reach => Width / 2f + Blur * 1.5f + 1f;
}

/// <summary>Whether a <see cref="Glow"/> stands or runs.</summary>
public enum GlowMotion : byte
{
    /// <summary>The whole border lit, the light diffused round it.</summary>
    Static,

    /// <summary>A comet of light with a fading tail runs along the border, clockwise.
    /// With reduced motion — the system's setting or <c>Motion.Preference</c> — the light
    /// stands, as <see cref="Static"/>.</summary>
    Comet,
}

/// <summary>
/// A halo — light round an element: a glow of its own shape behind it, or a round light
/// behind it, as a lamp gives.
/// </summary>
/// <param name="Color">The light's color; its alpha is the light's strength.</param>
/// <param name="Radius">How far the light reaches past the element.</param>
/// <param name="Shape">Of the element's shape, or round.</param>
/// <example>
/// <code>
/// icon.Halo = new Halo(Colors.Gold, 16);                     // the icon's own outline glows
/// avatar.Halo = new Halo(Colors.White, 40, HaloShape.Radial); // a lamp behind it
/// </code>
/// In a style sheet: <c>Halo: #FFD700 16</c>, <c>Halo: #FFFFFF 40 radial</c>.
/// </example>
public sealed record Halo(Color Color, float Radius = 16f, HaloShape Shape = HaloShape.Content)
{
    /// <summary>How far the light reaches past the element.</summary>
    internal float Reach => Shape == HaloShape.Content ? Radius * 1.5f + 1f : Radius + 1f;
}

/// <summary>The shape of a <see cref="Halo"/>.</summary>
public enum HaloShape : byte
{
    /// <summary>What the element draws — its text, its icon, its children — blurred and in
    /// the halo's color, behind it: a glyph glows round its own strokes.</summary>
    Content,

    /// <summary>A round light behind the element, from its middle out past its edges.</summary>
    Radial,
}
