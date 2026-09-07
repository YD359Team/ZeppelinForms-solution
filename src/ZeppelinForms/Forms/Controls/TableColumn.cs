using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls;

/// <summary>Столбец таблицы: заголовок, ширина и выравнивание содержимого.</summary>
public sealed class TableColumn
{
    public string? Header { get; set; }

    /// <summary>Auto — по самому широкому значению, звёздочка — доля остатка,
    /// число — фиксированная ширина в пикселях.</summary>
    public GridLength Width { get; set; } = GridLength.Auto;

    public HorizontalContentAlignment Align { get; set; } = HorizontalContentAlignment.Left;

    public TableColumn() { }

    public TableColumn(string? header, GridLength width) => (Header, Width) = (header, width);
}
