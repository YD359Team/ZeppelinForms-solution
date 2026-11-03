using System.Globalization;

namespace ZeppelinForms.Data;

/// <summary>
/// What a binding does with a value on its way to the element and back: a converter, a
/// format, the value written where there is none. What markup's
/// <c>{Bind Total, StringFormat='{0:C}', FallbackValue=—}</c> compiles to.
/// </summary>
/// <remarks>
/// <para>
/// The way of a value to the element, as in WPF and Avalonia:
/// </para>
/// <list type="number">
/// <item>The path gives no value — a null on the way, a data context of another type, a
/// stream with nothing in it yet: <see cref="FallbackValue"/>, or the property's
/// default.</item>
/// <item>The value is null: <see cref="TargetNullValue"/>, if set; otherwise null goes
/// on.</item>
/// <item><see cref="Converter"/> converts it; <see cref="Binding.UnsetValue"/> from it is
/// "no value", <see cref="Binding.DoNothing"/> leaves the element as it is.</item>
/// <item><see cref="StringFormat"/> makes it text: <c>"{0:N2} kg"</c>, or a format of the
/// value alone, <c>"N2"</c>.</item>
/// <item>It is converted to the property's type as any bound value is; one that can't be
/// gives <see cref="FallbackValue"/> where set — otherwise the element keeps its value,
/// as before.</item>
/// </list>
/// <para>
/// Back, for a binding both ways: the element's value equal to
/// <see cref="TargetNullValue"/> is null; <see cref="IValueConverter.ConvertBack"/>
/// converts the rest. A format is not read back.
/// </para>
/// </remarks>
public sealed class BindingOptions
{
    /// <summary>Converts the value both ways; null — the types' own conversion.</summary>
    public IValueConverter? Converter { get; init; }

    /// <summary>Given to <see cref="Converter"/> as its parameter.</summary>
    public object? ConverterParameter { get; init; }

    /// <summary>The culture of the converter and of the format; null — the current one,
    /// read on every value.</summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>Written when the path gives no value, or the value can't be made the
    /// property's type. <see cref="Binding.UnsetValue"/> — not set: the property's
    /// default, and a value that doesn't fit leaves the element as it is.</summary>
    public object? FallbackValue { get; init; } = Binding.UnsetValue;

    /// <summary>Written when the value is null. <see cref="Binding.UnsetValue"/> — not
    /// set: null goes on.</summary>
    public object? TargetNullValue { get; init; } = Binding.UnsetValue;

    /// <summary>Makes the value text: a composite format, <c>"{0:N2} kg"</c>, or the format
    /// of the value alone, <c>"N2"</c>. Null — none.</summary>
    public string? StringFormat { get; init; }

    internal bool HasFallbackValue => !ReferenceEquals(FallbackValue, Binding.UnsetValue);

    internal bool HasTargetNullValue => !ReferenceEquals(TargetNullValue, Binding.UnsetValue);

    internal CultureInfo CurrentCulture => Culture ?? CultureInfo.CurrentCulture;

    /// <summary>The value as text by <see cref="StringFormat"/>.</summary>
    internal object? Format(object? value)
    {
        if (StringFormat is not { Length: > 0 } format) return value;

        CultureInfo culture = CurrentCulture;

        // a brace makes it a composite format; anything else is the value's own format,
        // as WPF reads StringFormat=N2
        if (format.Contains('{', StringComparison.Ordinal))
            return string.Format(culture, format, value);

        return value is IFormattable formattable ? formattable.ToString(format, culture) : value?.ToString();
    }
}
