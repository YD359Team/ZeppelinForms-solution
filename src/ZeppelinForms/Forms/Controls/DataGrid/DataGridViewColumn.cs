using System.Diagnostics.CodeAnalysis;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>A grid column: the header, the width and the way to get to the value.</summary>
/// <remarks>
/// The value is reached through an object: any record, any value, a cast in the
/// delegate. <see cref="DataGridViewColumn{TItem, TValue}"/> is the same column
/// typed — no casts in the delegates, and no boxing of a value type in the cells
/// and in sorting. Both go into the same <see cref="DataGridView.Columns"/>.
/// </remarks>
public class DataGridViewColumn
{
    public DataGridViewColumn()
    {
    }

    /// <summary>For a typed column: the value is reached through its own delegate.</summary>
    [SetsRequiredMembers]
    protected DataGridViewColumn(Func<object, object?> value)
    {
        ArgumentNullException.ThrowIfNull(value);

        Value = value;
    }

    public string? Header { get; set; }

    /// <summary>Fixed — a width in logical units, star — a share of the remainder,
    /// Auto — by the header and the visible rows.</summary>
    /// <remarks>
    /// Auto is computed here differently than in Table: that one measures all rows,
    /// while the grid is virtualized, and walking the whole set on every layout
    /// would make virtualization pointless. So Auto looks at the header and at the
    /// rows in the visible range — that is, the column width may change while
    /// scrolling. If that gets in the way, use a fixed width.
    /// </remarks>
    public GridLength Width { get; set; } = GridLength.Star();

    public HorizontalContentAlignment Align { get; set; } = HorizontalContentAlignment.Left;

    /// <summary>How to get the value out of a data row.</summary>
    /// <remarks>
    /// A delegate rather than a property name: reflection for every cell of every
    /// frame is exactly the cost the text caches were introduced for in 0.10.0.
    /// And a Binding would mean a subscription per visible cell, which would have
    /// to be recreated on every scroll.
    /// </remarks>
    public required Func<object, object?> Value { get; init; }

    /// <summary>How to write the value back. Null — the column is read-only.</summary>
    public Action<object, object?>? SetValue { get; set; }

    /// <summary>How to turn the value into text. Null — ToString with the current culture.</summary>
    public Func<object?, string>? Format { get; set; }

    public bool CanSort { get; set; } = true;

    /// <summary>The comparison for sorting. Null — Comparer.Default by value.</summary>
    public IComparer<object?>? Comparer { get; set; }

    /// <summary>The text of the cell in a row: what is drawn, read out and copied.</summary>
    internal virtual string TextOf(object item)
    {
        object? value = Value(item);

        return Format is not null ? Format(value) : value?.ToString() ?? string.Empty;
    }

    /// <summary>The order of two rows by this column, as sorting sees it.</summary>
    internal virtual int CompareRows(object left, object right) =>
        (Comparer ?? Comparer<object?>.Default).Compare(Value(left), Value(right));
}
