using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>A column whose value is a set of options, checked in a
/// <see cref="CheckedComboBox"/>: the tags of an article, the days of a schedule.</summary>
/// <typeparam name="TItem">The type of the records in the grid's rows.</typeparam>
/// <typeparam name="TOption">The type of an option.</typeparam>
/// <remarks>
/// <para>
/// For an enum the options are every member of it, unless <see cref="Options"/> is
/// filled. The cell shows the checked ones apart by commas, each by
/// <see cref="OptionText"/>; the value written back lists them in the order of the options.
/// </para>
/// <para>Not sorted by: a set has no order of its own. A comparer makes it sortable.</para>
/// </remarks>
public class DataGridViewCheckedListColumn<TItem, TOption> : DataGridViewColumn<TItem, IReadOnlyList<TOption>>
{
    /// <param name="valueOf">How to get the checked options of a record.</param>
    /// <param name="valueOfExpression">The text of <paramref name="valueOf"/>, filled
    /// in by the compiler: the header, when the lambda reads one member.</param>
    [SetsRequiredMembers]
    public DataGridViewCheckedListColumn(Func<TItem, IReadOnlyList<TOption>> valueOf,
        [CallerArgumentExpression(nameof(valueOf))] string? valueOfExpression = null)
        : base(valueOf, valueOfExpression)
    {
        CanSort = false;
        Format = options => string.Join(", ", (options ?? []).Select(TextOfOption));
    }

    /// <summary>The options to check. Empty for an enum — every member of it.</summary>
    public List<TOption> Options { get; init; } = [];

    /// <summary>How to show an option. Null — its ToString.</summary>
    public Func<TOption, string>? OptionText { get; set; }

    internal IReadOnlyList<TOption> Choices => Options.Count > 0 ? Options : EnumMembers();

    protected internal override UIElement? CreateEditor(object item, string? typedText)
    {
        IReadOnlyList<TOption> choices = Choices;
        IReadOnlyList<TOption> current = TypedValue(item) ?? [];

        var list = new CheckedComboBox
        {
            DisplaySelector = option => option is TOption typed ? TextOfOption(typed) : string.Empty,
        };

        foreach (TOption choice in choices)
            list.Items.Add(choice!);

        for (int i = 0; i < choices.Count; i++)
            if (current.Contains(choices[i]))
                list.SetChecked(i, true);

        return list;
    }

    protected internal override bool TryGetEditedValue(UIElement editor, object item, out object? value)
    {
        if (editor is not CheckedComboBox list)
        {
            value = null;
            return false;
        }

        value = list.CheckedItems.OfType<TOption>().ToList();
        return true;
    }

    internal override int CompareRows(object left, object right) =>
        // a set has no order: without a comparer of sets the rows keep theirs
        base.Comparer is null ? 0 : base.CompareRows(left, right);

    private string TextOfOption(TOption option) =>
        OptionText is not null ? OptionText(option) : option?.ToString() ?? string.Empty;

    private static TOption[] EnumMembers()
    {
        Type type = typeof(TOption);

        if (!type.IsEnum) return [];

        Array raw = Enum.GetValuesAsUnderlyingType(type);
        var members = new TOption[raw.Length];

        for (int i = 0; i < raw.Length; i++)
            members[i] = (TOption)Enum.ToObject(type, raw.GetValue(i)!);

        return members;
    }

    internal override bool TakesTypedText => false;
}
