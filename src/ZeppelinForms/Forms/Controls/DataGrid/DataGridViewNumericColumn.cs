using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Numerics;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>A column of numbers, edited in a <see cref="NumericUpDown"/>: typed, or
/// stepped by the arrows and the wheel.</summary>
/// <typeparam name="TItem">The type of the records in the grid's rows.</typeparam>
/// <typeparam name="TValue">The number type: int, long, double, decimal and the like.</typeparam>
/// <remarks>The editor counts in decimal: a value beyond decimal's range — a huge
/// double — is brought into it, and back.</remarks>
public class DataGridViewNumericColumn<TItem, TValue> : DataGridViewColumn<TItem, TValue>
    where TValue : struct, INumber<TValue>, IMinMaxValue<TValue>
{
    /// <param name="valueOf">How to get the number out of a record.</param>
    /// <param name="valueOfExpression">The text of <paramref name="valueOf"/>, filled
    /// in by the compiler: the header, when the lambda reads one member.</param>
    [SetsRequiredMembers]
    public DataGridViewNumericColumn(Func<TItem, TValue> valueOf,
        [CallerArgumentExpression(nameof(valueOf))] string? valueOfExpression = null)
        : base(valueOf, valueOfExpression)
    {
        Align = HorizontalContentAlignment.Right;
    }

    /// <summary>The smallest value the editor takes. The type's least by default.</summary>
    public TValue Minimum { get; set; } = TValue.MinValue;

    /// <summary>The largest value the editor takes. The type's greatest by default.</summary>
    public TValue Maximum { get; set; } = TValue.MaxValue;

    /// <summary>How much an arrow or a wheel notch changes the value.</summary>
    public TValue Step { get; set; } = TValue.One;

    /// <summary>The digits after the separator the editor shows and keeps.</summary>
    public int DecimalPlaces { get; set; }

    protected internal override UIElement? CreateEditor(object item, string? typedText) => new NumericUpDown
    {
        Minimum = decimal.CreateSaturating(Minimum),
        Maximum = decimal.CreateSaturating(Maximum),
        Step = decimal.CreateSaturating(Step),
        DecimalPlaces = DecimalPlaces,
        Value = decimal.CreateSaturating(TypedValue(item)),
    };

    protected internal override bool TryGetEditedValue(UIElement editor, object item, out object? value)
    {
        if (editor is not NumericUpDown numeric)
        {
            value = null;
            return false;
        }

        // the digits typed and not yet taken by the editor itself — it takes them on
        // Enter and on losing the focus, and the grid ends the edit before either
        numeric.CommitEdit();

        value = TValue.CreateSaturating(numeric.Value);
        return true;
    }

    internal override bool TakesTypedText => false;
}
