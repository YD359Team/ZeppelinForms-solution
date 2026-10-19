using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>A grid column over records of a known type: the value, its writing back,
/// its text and its order are typed, without casts.</summary>
/// <typeparam name="TItem">The type of the records in the grid's rows.</typeparam>
/// <typeparam name="TValue">The type of the column's value.</typeparam>
/// <remarks>
/// <para>
/// The untyped column takes the value out as an object: a cast in every delegate,
/// and an int or a DateTime boxed for every visible cell of every frame and twice
/// for every comparison of a sort. Here the value goes from the record to the text
/// and to the comparer as it is.
/// </para>
/// <para>
/// A row whose record is not a <typeparamref name="TItem"/> — a grid with records of
/// several kinds — shows an empty cell in this column and sorts first, rather than
/// throwing in the middle of a frame.
/// </para>
/// <para>
/// Seen as a <see cref="DataGridViewColumn"/>, the column is the same column: its
/// <see cref="DataGridViewColumn.Value"/> boxes the typed value, and a format or
/// a comparer set through the untyped column takes over from the typed one.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// new DataGridViewColumn&lt;Game, int&gt;(game => game.Year)
/// {
///     Header = "Year",
///     Format = year => year.ToString(CultureInfo.InvariantCulture),
/// }
/// </code>
/// </example>
public partial class DataGridViewColumn<TItem, TValue> : DataGridViewColumn
{
    // the untyped delegates handed to the base column: one set from outside is
    // told apart from these, and is then the one that counts
    private Func<object?, string>? _typedFormat;
    private IComparer<object?>? _typedComparer;
    private Action<object, object?>? _typedSetValue;

    /// <param name="valueOf">How to get the value out of a record.</param>
    /// <param name="valueOfExpression">The text of <paramref name="valueOf"/>, filled
    /// in by the compiler: the header, when the lambda reads one member.</param>
    /// <remarks>
    /// <c>new DataGridViewColumn&lt;Process, long&gt;(process => process.WorkingSet64)</c>
    /// is headed "WorkingSet64" until <see cref="DataGridViewColumn.Header"/> is set — the
    /// way a WinForms grid heads the columns it makes from properties. A lambda that does
    /// more than read a member leaves the header empty.
    /// </remarks>
    [SetsRequiredMembers]
    public DataGridViewColumn(
        Func<TItem, TValue> valueOf,
        [CallerArgumentExpression(nameof(valueOf))] string? valueOfExpression = null)
        : base(item => item is TItem record ? valueOf(record) : null)
    {
        ValueOf = valueOf;
        Header = MemberNameOf(valueOfExpression);
    }

    /// <summary>"x => x.Name" → "Name"; anything more than reading one member of the
    /// parameter → null.</summary>
    internal static string? MemberNameOf(string? expression)
    {
        if (expression is null) return null;

        Match match = MemberAccess().Match(expression);

        return match.Success ? match.Groups["member"].Value : null;
    }

    [GeneratedRegex(@"^\s*(static\s+)?\(?\s*(?<parameter>@?\w+)\s*\)?\s*=>\s*\k<parameter>\s*\.\s*(?<member>\w+)\s*$")]
    private static partial Regex MemberAccess();

    /// <summary>How to get the value out of a record, typed. The untyped
    /// <see cref="DataGridViewColumn.Value"/> is this one, boxed.</summary>
    public Func<TItem, TValue> ValueOf { get; }

    /// <summary>How to write the value back. Null — the column is read-only.</summary>
    public new Action<TItem, TValue>? SetValue
    {
        get;
        set
        {
            field = value;

            _typedSetValue = value is null
                ? null
                : (item, written) =>
                {
                    if (item is TItem record) value(record, Unbox(written));
                };

            base.SetValue = _typedSetValue;
        }
    }

    /// <summary>How to turn the value into text. Null — ToString with the current culture.</summary>
    public new Func<TValue, string>? Format
    {
        get;
        set
        {
            field = value;
            _typedFormat = value is null ? null : boxed => value(Unbox(boxed));
            base.Format = _typedFormat;
        }
    }

    /// <summary>How to turn the text typed into a cell into a value. Null — the text as
    /// it is for a string; an enum by its name; a number, a date, a bool by the
    /// interface culture.</summary>
    /// <remarks>Throwing <see cref="FormatException"/> or <see cref="OverflowException"/>
    /// means the text isn't a value: the editor stays open on it.</remarks>
    public new Func<string, TValue>? Parse
    {
        get;
        set
        {
            field = value;
            base.Parse = value is null ? null : text => value(text);
        }
    }

    /// <summary>The comparison for sorting. Null — Comparer.Default of the value type.</summary>
    public new IComparer<TValue>? Comparer
    {
        get;
        set
        {
            field = value;

            _typedComparer = value is null
                ? null
                : Comparer<object?>.Create((left, right) => value.Compare(Unbox(left), Unbox(right)));

            base.Comparer = _typedComparer;
        }
    }

    internal override string TextOf(object item)
    {
        // a format set through the untyped column is the one asked for
        if (base.Format is { } format && !ReferenceEquals(format, _typedFormat))
            return base.TextOf(item);

        if (item is not TItem record) return string.Empty;

        TValue value = ValueOf(record);

        return Format is not null ? Format(value) : value?.ToString() ?? string.Empty;
    }

    internal override int CompareRows(object left, object right)
    {
        if (base.Comparer is { } untyped && !ReferenceEquals(untyped, _typedComparer))
            return base.CompareRows(left, right);

        // a record of another kind has no value here: it goes first, as a null does
        return (left, right) switch
        {
            (TItem l, TItem r) => (Comparer ?? Comparer<TValue>.Default).Compare(ValueOf(l), ValueOf(r)),
            (TItem, _) => 1,
            (_, TItem) => -1,
            _ => 0,
        };
    }

    internal override Type? EditedType(object item) => typeof(TValue);

    /// <summary>The value of a row's record; the default for a record of another kind.</summary>
    private protected TValue TypedValue(object item) => item is TItem record ? ValueOf(record) : default!;

    /// <summary>A value that came through an object: null for a value type is its default.</summary>
    private protected static TValue Unbox(object? value) => value is TValue typed ? typed : default!;
}
