using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>A grid column: the header, the width and the way to get to the value.</summary>
/// <remarks>
/// <para>
/// The value is reached through an object: any record, any value, a cast in the
/// delegate. <see cref="DataGridViewColumn{TItem, TValue}"/> is the same column
/// typed — no casts in the delegates, and no boxing of a value type in the cells
/// and in sorting. Both go into the same <see cref="DataGridView.Columns"/>.
/// </para>
/// <para>
/// A plain column shows its value as text and, with <see cref="SetValue"/>, is edited
/// in a text box. The other kinds — a check box, a picture, a date, a drop-down,
/// a number, a list of checked options — are columns of their own deriving from the
/// typed one, and a column of a kind the framework doesn't have overrides
/// <see cref="DrawCell"/>, <see cref="CreateEditor"/>, <see cref="TryGetEditedValue"/>
/// and <see cref="TryActivate"/>.
/// </para>
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

    /// <summary>How to turn the text typed into a cell into a value. Null — the text as
    /// it is for a string; an enum by its name; a number, a date, a bool by the
    /// interface culture. A value of any other type needs this to be edited as text.</summary>
    /// <remarks>Throwing <see cref="FormatException"/> or <see cref="OverflowException"/>
    /// means the text isn't a value: the editor stays open on it.</remarks>
    public Func<string, object?>? Parse { get; set; }

    /// <summary>Not edited, even with <see cref="SetValue"/>.</summary>
    public bool ReadOnly { get; set; }

    public bool CanSort { get; set; } = true;

    /// <summary>The comparison for sorting. Null — Comparer.Default by value.</summary>
    public IComparer<object?>? Comparer { get; set; }

    /// <summary>Whether typing on the grid begins editing a cell of the column with what
    /// is typed: a column edited in a text box. The other kinds turn it off.</summary>
    internal virtual bool TakesTypedText => true;

    /// <summary>Whether a cell of the column can take a value.</summary>
    internal bool IsEditable => SetValue is not null && !ReadOnly;

    /// <summary>The text of the cell in a row: what is drawn, read out and copied.</summary>
    internal virtual string TextOf(object item)
    {
        object? value = Value(item);

        return Format is not null ? Format(value) : value?.ToString() ?? string.Empty;
    }

    /// <summary>The order of two rows by this column, as sorting sees it.</summary>
    internal virtual int CompareRows(object left, object right) =>
        (Comparer ?? Comparer<object?>.Default).Compare(Value(left), Value(right));

    /// <summary>The type the edited text turns into; null — a string.</summary>
    /// <remarks>An untyped column knows only the value there is now.</remarks>
    internal virtual Type? EditedType(object item) => Value(item)?.GetType();

    // ===== the cell's behaviour: what a column of another kind overrides =====

    /// <summary>Draw a cell's content inside its padding. By default — its text.</summary>
    /// <param name="g">The grid's graphics, clipped to the rows.</param>
    /// <param name="bounds">The cell less the grid's <see cref="DataGridView.CellPadding"/>.</param>
    /// <param name="item">The record of the row.</param>
    /// <param name="paint">The row's colors and font, and the grid for its own colors.</param>
    protected internal virtual void DrawCell(Graphics g, Rectangle bounds, object item, DataGridViewCellPaint paint)
    {
        ArgumentNullException.ThrowIfNull(g);

        g.DrawText(TextOf(item), bounds, paint.TextColor, paint.Font, Align, VerticalContentAlignment.Center);
    }

    /// <summary>The control a cell is edited in, made as editing begins and put over
    /// the cell. Null — the cell isn't edited in a control: a check box is toggled
    /// in place, a picture isn't edited.</summary>
    /// <param name="item">The record of the row.</param>
    /// <param name="typedText">The text typed on the grid that began the editing —
    /// it replaces the value, as typing over a spreadsheet cell does; null — the editing
    /// began otherwise, by F2 or a double click. A column that isn't edited as text
    /// ignores it.</param>
    protected internal virtual UIElement? CreateEditor(object item, string? typedText)
    {
        var editor = new TextBox { Text = typedText ?? EditTextOf(item) };

        // typed over: the caret after what was typed. Opened: the whole value, to be
        // replaced by typing or kept by moving the caret
        if (typedText is null) editor.SelectAll();
        else editor.Select(typedText.Length, 0);

        return editor;
    }

    /// <summary>The value the editor holds. False — what it holds isn't a value of the
    /// column (a number that doesn't parse): the editor stays open.</summary>
    protected internal virtual bool TryGetEditedValue(UIElement editor, object item, out object? value)
    {
        if (editor is not TextBox box)
        {
            value = null;
            return false;
        }

        string text = box.Text ?? string.Empty;

        if (Parse is null) return TryConvert(text, EditedType(item), Localization.Culture, out value);

        try
        {
            value = Parse(text);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>A click on a cell, or Space on it, changes the value at once — a check box
    /// is toggled. False — the cell has no such action; true — the value to write.</summary>
    /// <remarks>Called only for an editable column; the grid writes the value, with
    /// its events, as an edit.</remarks>
    protected internal virtual bool TryActivate(object item, out object? value)
    {
        value = null;
        return false;
    }

    /// <summary>The value as the text editor starts with it: unformatted — a format
    /// for the eye ("2,004 MB") would not parse back.</summary>
    internal string EditTextOf(object item) => Value(item) switch
    {
        null => string.Empty,
        IFormattable formattable => formattable.ToString(null, Localization.Culture),
        { } value => value.ToString() ?? string.Empty,
    };

    /// <summary>Text into a value of a type, without reflection: a string as it is, an
    /// enum by its name, the IConvertible types — numbers, dates, bools — by the culture.</summary>
    internal static bool TryConvert(string text, Type? type, IFormatProvider culture, out object? value)
    {
        value = null;

        if (type is null || type == typeof(string))
        {
            value = text;
            return true;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            // an empty cell of a nullable column is no value, not a mistake
            if (string.IsNullOrWhiteSpace(text)) return true;

            type = underlying;
        }

        if (type.IsEnum) return Enum.TryParse(type, text.Trim(), ignoreCase: true, out value);

        if (!typeof(IConvertible).IsAssignableFrom(type)) return false;

        try
        {
            value = Convert.ChangeType(text.Trim(), type, culture);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or InvalidCastException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>The interface culture, for the kinds of columns that format by it.</summary>
    private protected static CultureInfo Culture => Localization.Culture;
}
