using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Animation;

/// <summary>
/// Knows how to compute an intermediate value between two. A registry by type:
/// a transition is declared on a property, but how to blend its values
/// is a question of the type, not of the property.
/// </summary>
/// <remarks>
/// A type without an interpolator gets no transitions: jumping to the final
/// value is not a transition but the absence of one, and silently substituting
/// one for the other would leave the control author guessing why nothing animates.
/// </remarks>
public static class Interpolator
{
    private static readonly Dictionary<Type, object> Registry = [];

    static Interpolator()
    {
        Register<float>(Interpolators.Float);
        Register<double>(static (a, b, t) => a + (b - a) * t);
        Register<Color>(Interpolators.Color);
        Register<Point>(Interpolators.Point);
        Register<Size>(Interpolators.Size);

        Register<Thickness>(static (a, b, t) => new Thickness(
            Interpolators.Float(a.Left, b.Left, t),
            Interpolators.Float(a.Top, b.Top, t),
            Interpolators.Float(a.Right, b.Right, t),
            Interpolators.Float(a.Bottom, b.Bottom, t)));

        Register<CornerRadius>(static (a, b, t) => new CornerRadius(
            Interpolators.Float(a.TopLeft, b.TopLeft, t),
            Interpolators.Float(a.TopRight, b.TopRight, t),
            Interpolators.Float(a.BottomRight, b.BottomRight, t),
            Interpolators.Float(a.BottomLeft, b.BottomLeft, t)));
    }

    /// <summary>Teach transitions a new type. Your own registration overrides
    /// the built-in one — for cases where blending must work differently:
    /// colors, for example, sometimes need to be blended in a different color space.</summary>
    public static void Register<T>(Func<T, T, float, T> interpolate) =>
        Registry[typeof(T)] = interpolate;

    /// <summary>The type's interpolator, or null if there is none.</summary>
    public static Func<T, T, float, T>? Find<T>() =>
        Registry.TryGetValue(typeof(T), out object? found)
            ? (Func<T, T, float, T>)found
            : null;

    public static bool Supports(Type type) => Registry.ContainsKey(type);
}