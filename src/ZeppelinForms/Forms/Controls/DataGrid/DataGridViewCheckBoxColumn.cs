using System.Diagnostics.CodeAnalysis;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>A column of check boxes: a click on a cell, or Space on it, toggles the
/// value at once — no editor opens.</summary>
/// <typeparam name="TItem">The type of the records in the grid's rows.</typeparam>
/// <remarks>The box takes the grid's <see cref="DataGridView.CheckColor"/> and
/// <see cref="DataGridView.CheckGlyphColor"/>, which the themes set as they do a
/// <see cref="CheckBox"/>'s. Without <see cref="DataGridViewColumn.SetValue"/> the
/// boxes only show.</remarks>
public class DataGridViewCheckBoxColumn<TItem> : DataGridViewColumn<TItem, bool>
{
    /// <param name="valueOf">How to get the value out of a record.</param>
    [SetsRequiredMembers]
    public DataGridViewCheckBoxColumn(Func<TItem, bool> valueOf) : base(valueOf)
    {
        Align = HorizontalContentAlignment.Center;
        Width = GridLength.Fixed(70);
    }

    /// <summary>The side of the box; it is never taller than the cell.</summary>
    public float BoxSize { get; set; } = 16f;

    protected internal override void DrawCell(Graphics g, Rectangle bounds, object item, DataGridViewCellPaint paint)
    {
        ArgumentNullException.ThrowIfNull(g);

        if (item is not TItem) return;

        float size = Math.Min(BoxSize, bounds.Height);

        float x = Align switch
        {
            HorizontalContentAlignment.Left => bounds.X,
            HorizontalContentAlignment.Right => bounds.X + bounds.Width - size,
            _ => bounds.X + (bounds.Width - size) / 2f,
        };

        var box = new Rectangle(new Point(x, bounds.Y + (bounds.Height - size) / 2f), new Size(size, size));
        var radius = new CornerRadius(size * 0.2f);
        DataGridView grid = paint.Grid;

        if (TypedValue(item))
        {
            g.FillRoundRectangle(box, radius, grid.CheckColor);

            // fractions of the square's side, as a CheckBox draws its mark
            ReadOnlySpan<Point> mark =
            [
                new(box.X + box.Width * 0.22f, box.Y + box.Height * 0.52f),
                new(box.X + box.Width * 0.42f, box.Y + box.Height * 0.72f),
                new(box.X + box.Width * 0.78f, box.Y + box.Height * 0.30f),
            ];

            g.DrawPolyline(mark, grid.CheckGlyphColor, box.Width * 0.14f);
            return;
        }

        // an empty box in the row's text color, faded: it reads on a selected row,
        // a striped one and a dark theme alike
        g.DrawRoundRectangle(box, radius, paint.TextColor.WithA(150), 1f);
    }

    /// <summary>No editor: the value is toggled in place.</summary>
    protected internal override UIElement? CreateEditor(object item, string? typedText) => null;

    protected internal override bool TryActivate(object item, out object? value)
    {
        value = !TypedValue(item);
        return item is TItem;
    }

    internal override bool TakesTypedText => false;
}
