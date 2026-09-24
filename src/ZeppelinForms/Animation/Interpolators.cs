using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Animation;

public static class Interpolators
{
    public static float Float(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Blends channel by channel. Each channel is rounded and clamped
    /// to [0; 255]: an easing curve passed by the user may overshoot, a spring
    /// or a "back" curve goes past the target and returns, and a float → byte
    /// cast outside the byte range is left unspecified by the language —
    /// CoreCLR and Mono (WASM, Android) are not obliged to agree on the result.</summary>
    public static Color Color(Color a, Color b, float t) => new(
        Channel(a.A, b.A, t),
        Channel(a.R, b.R, t),
        Channel(a.G, b.G, t),
        Channel(a.B, b.B, t));

    public static Point Point(Point a, Point b, float t) =>
        new(Float(a.X, b.X, t), Float(a.Y, b.Y, t));

    public static Size Size(Size a, Size b, float t) =>
        new(Float(a.Width, b.Width, t), Float(a.Height, b.Height, t));

    // rounding instead of truncation: truncation biases every intermediate
    // value toward zero, so a fade lingers a shade darker than it should
    private static byte Channel(byte a, byte b, float t) =>
        (byte)Math.Clamp(MathF.Round(Float(a, b, t)), 0f, 255f);
}