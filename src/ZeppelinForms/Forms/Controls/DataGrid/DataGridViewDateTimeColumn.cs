using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>A column of dates, edited in a <see cref="DateTimePicker"/>.</summary>
/// <typeparam name="TItem">The type of the records in the grid's rows.</typeparam>
/// <remarks>The picker picks a day: the time of day the value had is kept.</remarks>
public class DataGridViewDateTimeColumn<TItem> : DataGridViewColumn<TItem, DateTime>
{
    /// <param name="valueOf">How to get the date out of a record.</param>
    /// <param name="valueOfExpression">The text of <paramref name="valueOf"/>, filled
    /// in by the compiler: the header, when the lambda reads one member.</param>
    [SetsRequiredMembers]
    public DataGridViewDateTimeColumn(Func<TItem, DateTime> valueOf,
        [CallerArgumentExpression(nameof(valueOf))] string? valueOfExpression = null)
        : base(valueOf, valueOfExpression)
    {
        Align = HorizontalContentAlignment.Center;

        // read when drawn, so DateFormat set after this and the language switched
        // later both show
        Format = date => date.ToString(DateFormat ?? "d", Culture);
    }

    /// <summary>The date format, of the cell and of the picker. Null — the interface
    /// culture's short date.</summary>
    public string? DateFormat { get; set; }

    protected internal override UIElement? CreateEditor(object item, string? typedText)
    {
        var picker = new DateTimePicker { Format = DateFormat };
        picker.SetValue(TypedValue(item));

        return picker;
    }

    protected internal override bool TryGetEditedValue(UIElement editor, object item, out object? value)
    {
        if (editor is not DateTimePicker picker)
        {
            value = null;
            return false;
        }

        // the day picked, at the time of day the value had
        value = picker.Value.Date + TypedValue(item).TimeOfDay;
        return true;
    }

    internal override bool TakesTypedText => false;
}
