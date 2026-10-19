using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>A column of pictures: an icon of a process, a thumbnail of a file. Shown,
/// not edited, and not sorted by.</summary>
/// <typeparam name="TItem">The type of the records in the grid's rows.</typeparam>
/// <remarks>The picture is fitted into the cell whole, its proportions kept. Its text —
/// what a screen reader reads and Ctrl+C copies — is empty unless
/// <see cref="DataGridViewColumn{TItem, TValue}.Format"/> gives one.</remarks>
public class DataGridViewImageColumn<TItem> : DataGridViewColumn<TItem, Image?>
{
    /// <param name="valueOf">How to get the picture of a record; null — none.</param>
    /// <param name="valueOfExpression">The text of <paramref name="valueOf"/>, filled
    /// in by the compiler: the header, when the lambda reads one member.</param>
    [SetsRequiredMembers]
    public DataGridViewImageColumn(Func<TItem, Image?> valueOf,
        [CallerArgumentExpression(nameof(valueOf))] string? valueOfExpression = null)
        : base(valueOf, valueOfExpression)
    {
        CanSort = false;
        ReadOnly = true;
        Width = GridLength.Fixed(40);

        // the picture's ToString is its type name: nobody wants that read out
        Format = _ => string.Empty;
    }

    /// <summary>How the picture fills the cell. Zoom — whole, its proportions kept.</summary>
    public ImageLayout Layout { get; set; } = ImageLayout.Zoom;

    protected internal override void DrawCell(Graphics g, Rectangle bounds, object item, DataGridViewCellPaint paint)
    {
        ArgumentNullException.ThrowIfNull(g);

        if (TypedValue(item) is { } image)
            g.DrawImage(bounds, image, layout: Layout);
    }

    protected internal override UIElement? CreateEditor(object item, string? typedText) => null;

    internal override bool TakesTypedText => false;
}
