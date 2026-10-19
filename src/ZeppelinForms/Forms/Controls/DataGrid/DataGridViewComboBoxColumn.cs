using System.Diagnostics.CodeAnalysis;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>A column whose value is one of a list, picked in a <see cref="ComboBox"/>:
/// a status, a category, a member of an enum.</summary>
/// <typeparam name="TItem">The type of the records in the grid's rows.</typeparam>
/// <typeparam name="TValue">The type of the values.</typeparam>
/// <remarks>For an enum the list is every member of it, unless
/// <see cref="Values"/> is filled. The values are shown in the cell and in the list
/// alike, by <see cref="DataGridViewColumn{TItem, TValue}.Format"/>.</remarks>
public class DataGridViewComboBoxColumn<TItem, TValue> : DataGridViewColumn<TItem, TValue>
{
    /// <param name="valueOf">How to get the value out of a record.</param>
    [SetsRequiredMembers]
    public DataGridViewComboBoxColumn(Func<TItem, TValue> valueOf) : base(valueOf)
    {
    }

    /// <summary>The values to pick from. Empty for an enum — every member of it.</summary>
    public List<TValue> Values { get; init; } = [];

    /// <summary>The values the list offers: the ones given, or an enum's members.</summary>
    internal IReadOnlyList<TValue> Choices => Values.Count > 0 ? Values : EnumMembers();

    protected internal override UIElement? CreateEditor(object item, string? typedText)
    {
        IReadOnlyList<TValue> choices = Choices;
        TValue current = TypedValue(item);

        var combo = new ComboBox
        {
            DisplaySelector = choice => TextOfValue(choice is TValue value ? value : default!),
        };

        foreach (TValue choice in choices)
            combo.Items.Add(choice!);

        for (int i = 0; i < choices.Count; i++)
        {
            if (!EqualityComparer<TValue>.Default.Equals(choices[i], current)) continue;

            combo.SelectedIndex = i;
            break;
        }

        return combo;
    }

    protected internal override bool TryGetEditedValue(UIElement editor, object item, out object? value)
    {
        // nothing picked keeps the value there was
        value = editor is ComboBox { SelectedItem: TValue picked } ? picked : TypedValue(item);
        return editor is ComboBox;
    }

    private string TextOfValue(TValue value) =>
        Format is not null ? Format(value) : value?.ToString() ?? string.Empty;

    /// <summary>Every member of the enum the values are — of a nullable one too;
    /// none when they aren't an enum.</summary>
    /// <remarks>Through the underlying values rather than Enum.GetValues(Type),
    /// which makes an array of a type known only at run time and is no friend of AOT.</remarks>
    private static TValue[] EnumMembers()
    {
        Type type = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);

        if (!type.IsEnum) return [];

        Array raw = Enum.GetValuesAsUnderlyingType(type);
        var members = new TValue[raw.Length];

        for (int i = 0; i < raw.Length; i++)
            members[i] = (TValue)Enum.ToObject(type, raw.GetValue(i)!);

        return members;
    }

    internal override bool TakesTypedText => false;
}
