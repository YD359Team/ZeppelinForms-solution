using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

public class Grid : PanelControl
{
    public List<GridLength> RowDefinitions { get; init; } = [];
    public List<GridLength> ColumnDefinitions { get; init; } = [];

    protected override void ArrangeChildren()
    {
        var content = ContentBounds;
        float[] rowHeights = ResolveSizes(RowDefinitions, content.Height);
        float[] colWidths = ResolveSizes(ColumnDefinitions, content.Width);

        foreach (var child in Children)
        {
            var (row, col) = child is IGridPlaceable p ? (p.Row, p.Column) : (0, 0);
            var m = child.Margin;

            float cellX = content.X + colWidths.Take(col).Sum();
            float cellY = content.Y + rowHeights.Take(row).Sum();

            child.Position = new Point(cellX + m.Left, cellY + m.Top);
            child.Size = new Size(
                Math.Max(0, colWidths[col] - m.Horizontal),
                Math.Max(0, rowHeights[row] - m.Vertical));
        }
    }

    private static float[] ResolveSizes(List<GridLength> defs, float total)
    {
        float fixedSum = defs.Where(d => !d.IsStar).Sum(d => d.Value);
        float starSum = defs.Where(d => d.IsStar).Sum(d => d.Value);
        float remaining = Math.Max(0, total - fixedSum);

        return defs.Select(d => d.IsStar
            ? (starSum > 0 ? remaining * (d.Value / starSum) : 0)
            : d.Value).ToArray();
    }

    public override void Draw(Graphics g)
    {
        return;
    }
}

public readonly record struct GridLength(float Value, bool IsStar)
{
    public static GridLength Fixed(float px) => new(px, false);
    public static GridLength Star(float weight = 1) => new(weight, true);
}