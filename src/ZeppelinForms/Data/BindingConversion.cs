using System.Collections.Concurrent;
using System.Globalization;

namespace ZeppelinForms.Data;

/// <summary>
/// The conversions between a binding's two ends, made once per pair of types and kept:
/// what used to be worked out from the types on every write — is it the type already,
/// is it a nullable, an enum, which conversion — is a delegate looked up by the pair.
/// </summary>
/// <remarks>
/// <para>
/// No reflection over members: the checks are on <see cref="Type"/> alone —
/// <see cref="Type.IsAssignableFrom(Type)"/>, <see cref="Type.IsEnum"/>, the type code —
/// so trimming and native AOT need nothing kept for them, as before.
/// </para>
/// <para>
/// The culture is read on every conversion, not when the delegate is made: it may be
/// switched while the application runs, and a number typed after that is read in the
/// new one.
/// </para>
/// <para>
/// A conversion that can't be made throws, as <see cref="Convert.ChangeType(object, Type, IFormatProvider)"/>
/// did: <see cref="Binding"/> tells a value that doesn't fit from a bug by the
/// exception's type.
/// </para>
/// <para>
/// The table is never emptied: its keys are pairs of types, a few dozen in an
/// application. Should previews ever load projects into collectible contexts, it
/// would have to become weak by the source type.
/// </para>
/// </remarks>
internal static class BindingConversion
{
    private static readonly ConcurrentDictionary<(Type Source, Type Target), Func<object, object?>> s_conversions = new();

    /// <summary>The value fits as it is.</summary>
    internal static readonly Func<object, object?> Identity = static value => value;

    /// <summary>The pairs met so far. For tests.</summary>
    internal static int Count => s_conversions.Count;

    /// <summary>The conversion of a value of <paramref name="source"/> — its runtime
    /// type — to <paramref name="target"/>.</summary>
    public static Func<object, object?> For(Type source, Type target) =>
        s_conversions.GetOrAdd((source, target), static key => Build(key.Source, key.Target));

    private static Func<object, object?> Build(Type source, Type target)
    {
        if (target.IsAssignableFrom(source)) return Identity;

        // a boxed T? is a boxed T: a value of T goes into a T? property as it is
        Type underlying = Nullable.GetUnderlyingType(target) ?? target;

        if (!ReferenceEquals(underlying, target) && underlying.IsAssignableFrom(source))
            return Identity;

        if (underlying.IsEnum)
        {
            // an integer goes in as the member's value, with no text in between; any
            // other value — a name, another enum — goes by name, as it always did:
            // Color.Red becomes Shade.Red, not the member that has the same number
            if (!source.IsEnum && IsIntegral(Type.GetTypeCode(source)))
                return value => Enum.ToObject(underlying, value);

            return value => Enum.Parse(underlying, value.ToString()!);
        }

        if (ReferenceEquals(underlying, typeof(string)))
        {
            // the same text ChangeType gave for numbers, dates and enums — and now also
            // for what has no IConvertible: a Guid, a TimeSpan, the application's own
            // types, which used to throw and left the element empty
            return static value => value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.CurrentCulture)
                : value.ToString();
        }

        TypeCode code = Type.GetTypeCode(underlying);

        // a number, a bool, a date: by the type code, without looking the target type
        // up again on every call. A value shown to the user and typed by them: their
        // culture, as before, now said aloud
        if (code is not (TypeCode.Object or TypeCode.Empty or TypeCode.DBNull))
            return value => System.Convert.ChangeType(value, code, CultureInfo.CurrentCulture);

        // anything else: what the value's IConvertible can do, if it has one — usually
        // an InvalidCastException, as before
        return value => System.Convert.ChangeType(value, underlying, CultureInfo.CurrentCulture);
    }

    private static bool IsIntegral(TypeCode code) => code is >= TypeCode.SByte and <= TypeCode.UInt64;
}