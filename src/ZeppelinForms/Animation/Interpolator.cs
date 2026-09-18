using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Animation;

/// <summary>
/// Кто умеет считать промежуточное значение между двумя. Реестр по типу:
/// переход объявляется на свойстве, а как смешивать его значения — вопрос
/// типа, а не свойства.
/// </summary>
/// <remarks>
/// Тип без интерполятора переходов не получает: перескок в конечное
/// значение — это не переход, а его отсутствие, и молча подменять одно
/// другим значит оставить автора контрола гадать, почему не анимируется.
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

    /// <summary>Научить переходы новому типу. Своё объявление перекрывает
    /// встроенное — на случай, когда смешивать надо иначе: цвета, например,
    /// бывает нужно вести через другое цветовое пространство.</summary>
    public static void Register<T>(Func<T, T, float, T> interpolate) =>
        Registry[typeof(T)] = interpolate;

    /// <summary>Интерполятор типа или null, если такого нет.</summary>
    public static Func<T, T, float, T>? Find<T>() =>
        Registry.TryGetValue(typeof(T), out object? found)
            ? (Func<T, T, float, T>)found
            : null;

    public static bool Supports(Type type) => Registry.ContainsKey(type);
}