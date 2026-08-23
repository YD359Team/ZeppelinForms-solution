using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

public class Grid : PanelControl
{
    public List<GridLength> RowDefinitions { get; init; } = [];
    public List<GridLength> ColumnDefinitions { get; init; } = [];

    public override void Draw(Graphics g) { }

    protected override Size MeasureOverride(Size availableSize)
    {
        var content = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        float[] rowHeights = ResolveSizes(RowDefinitions, content.Height);
        float[] colWidths = ResolveSizes(ColumnDefinitions, content.Width);

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            var m = child.Margin;

            var cellSize = new Size(
                Math.Max(0, colWidths.ElementAtOrDefault(child.Column) - m.Horizontal),
                Math.Max(0, rowHeights.ElementAtOrDefault(child.Row) - m.Vertical));

            // Auto-строк/колонок пока нет — но Measure всё равно нужно
            // прогнать вниз по дереву, иначе у внуков не будет DesiredSize
            child.Measure(cellSize);
        }

        return ResolveSize(
            new Size(content.Width + Padding.Horizontal, content.Height + Padding.Vertical),
            availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var content = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical)));

        float[] rowHeights = ResolveSizes(RowDefinitions, content.Height);
        float[] colWidths = ResolveSizes(ColumnDefinitions, content.Width);

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            var m = child.Margin;

            float cellX = content.X + colWidths.Take(child.Column).Sum();
            float cellY = content.Y + rowHeights.Take(child.Row).Sum();

            var rect = new Rectangle(
                new Point(cellX + m.Left, cellY + m.Top),
                new Size(
                    Math.Max(0, colWidths.ElementAtOrDefault(child.Column) - m.Horizontal),
                    Math.Max(0, rowHeights.ElementAtOrDefault(child.Row) - m.Vertical)));

            child.Arrange(rect);
        }

        return finalSize;
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
}

public readonly record struct GridLength(float Value, bool IsStar)
{
    public static GridLength Fixed(float px) => new(px, false);
    public static GridLength Star(float weight = 1) => new(weight, true);
}