using System.Globalization;

namespace ZeppelinForms.Data;

/// <summary>
/// A conversion between a binding's source and its element that the types alone don't
/// say: a flag shown as its opposite, a number as a color, a state as a word.
/// </summary>
/// <remarks>
/// <para>
/// The same contract as WPF's and Avalonia's converters: <see cref="Convert"/> goes from
/// the source to the element, <see cref="ConvertBack"/> back for a binding both ways.
/// Return <see cref="Binding.UnsetValue"/> for "no value" — the binding writes its
/// <see cref="BindingOptions.FallbackValue"/>, or the property's default — and
/// <see cref="Binding.DoNothing"/> to leave the other end as it is.
/// </para>
/// <para>
/// What a converter returns that isn't the property's type is converted as any bound value
/// is: a number returned for a text property becomes its text.
/// </para>
/// </remarks>
public interface IValueConverter
{
    /// <param name="value">The source's value; null where the source has null.</param>
    /// <param name="targetType">The type of the element's property.</param>
    /// <param name="parameter">The binding's <see cref="BindingOptions.ConverterParameter"/>.</param>
    /// <param name="culture">The culture to read and write text in: the current one.</param>
    object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);

    /// <param name="value">The element's value.</param>
    /// <param name="targetType">The type of the source's property.</param>
    /// <param name="parameter">The binding's <see cref="BindingOptions.ConverterParameter"/>.</param>
    /// <param name="culture">The culture to read and write text in: the current one.</param>
    object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture);
}

/// <summary>A converter made of lambdas: the conversion there is no class for.</summary>
/// <remarks>A value that isn't a <typeparamref name="TIn"/> — a null for a value type
/// included — is passed as <c>default</c>.</remarks>
/// <example><c>new FuncValueConverter&lt;int, string&gt;(count =&gt; count == 1 ? "1 item" : $"{count} items")</c></example>
public sealed class FuncValueConverter<TIn, TOut> : IValueConverter
{
    private readonly Func<TIn?, TOut> _convert;
    private readonly Func<TOut?, TIn>? _convertBack;

    /// <param name="convert">From the source to the element.</param>
    public FuncValueConverter(Func<TIn?, TOut> convert)
        : this(convert, null)
    {
    }

    /// <param name="convert">From the source to the element.</param>
    /// <param name="convertBack">Back, for a binding both ways; null — the element's
    /// changes stay in it.</param>
    public FuncValueConverter(Func<TIn?, TOut> convert, Func<TOut?, TIn>? convertBack)
    {
        ArgumentNullException.ThrowIfNull(convert);

        _convert = convert;
        _convertBack = convertBack;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        _convert(value is TIn typed ? typed : default);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        _convertBack is null ? Binding.DoNothing : _convertBack(value is TOut typed ? typed : default);
}

/// <summary>Converters of flags, for markup: <c>IsVisible="{Bind IsBusy, Converter=BoolConverters.Not}"</c>.</summary>
public static class BoolConverters
{
    /// <summary>The opposite flag, both ways; a value that isn't a flag gives none.</summary>
    public static IValueConverter Not { get; } = new FuncValueConverter<bool, bool>(value => !value, value => !value);
}

/// <summary>Converters of any value, for markup: <c>IsVisible="{Bind Error, Converter=ObjectConverters.IsNotNull}"</c>.</summary>
public static class ObjectConverters
{
    /// <summary>Whether the value is null. One way.</summary>
    public static IValueConverter IsNull { get; } = new FuncValueConverter<object, bool>(value => value is null);

    /// <summary>Whether the value is there. One way.</summary>
    public static IValueConverter IsNotNull { get; } = new FuncValueConverter<object, bool>(value => value is not null);
}

/// <summary>Converters of text, for markup: <c>IsEnabled="{Bind Name, Converter=StringConverters.IsNotNullOrEmpty}"</c>.</summary>
public static class StringConverters
{
    /// <summary>Whether the text is null or empty. One way.</summary>
    public static IValueConverter IsNullOrEmpty { get; } = new FuncValueConverter<string, bool>(string.IsNullOrEmpty);

    /// <summary>Whether there is text. One way.</summary>
    public static IValueConverter IsNotNullOrEmpty { get; } =
        new FuncValueConverter<string, bool>(value => !string.IsNullOrEmpty(value));
}
