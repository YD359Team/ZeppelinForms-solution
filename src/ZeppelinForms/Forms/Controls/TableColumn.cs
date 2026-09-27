using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls;

/// <summary>A table column: header, width and content alignment.</summary>
public sealed class TableColumn
{
    public string? Header { get; set; }

    /// <summary>Auto — by the widest value, star — a share of the remainder,
    /// a number — a fixed width in pixels.</summary>
    public GridLength Width { get; set; } = GridLength.Auto;

    public HorizontalContentAlignment Align { get; set; } = HorizontalContentAlignment.Left;

    public TableColumn() { }

    public TableColumn(string? header, GridLength width) => (Header, Width) = (header, width);
}