using System.Runtime.CompilerServices;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

public class Grid : PanelControl
{
    private float[] _rowHeights = [];
    private float[] _colWidths = [];

    public string Columns { set => ColumnDefinitions = GridLength.Parse(value); }
    public string Rows { set => RowDefinitions = GridLength.Parse(value); }

    public List<GridLength> RowDefinitions { get; private set; } = [];
    public List<GridLength> ColumnDefinitions { get; private set; } = [];

    public override void Draw(Graphics g) { }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var content = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        _colWidths = ResolveTracks(ColumnDefinitions, content.Width, horizontal: true);
        _rowHeights = ResolveTracks(RowDefinitions, content.Height, horizontal: false);

        foreach (UIElement child in Children)
        {
            if (!child.IsVisible) continue;

            Thickness m = child.Margin;

            child.Measure(new Size(
                Math.Max(0, SpanExtent(_colWidths, child.Column, child.ColumnSpan) - m.Horizontal),
                Math.Max(0, SpanExtent(_rowHeights, child.Row, child.RowSpan) - m.Vertical)));
        }

        return ResolveSize(
            new Size(_colWidths.Sum() + Padding.Horizontal, _rowHeights.Sum() + Padding.Vertical),
            availableSize);
    }

    protected override void ArrangeContentOverride(Size finalSize)
    {
        var content = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical)));

        float[] colWidths = RedistributeStars(_colWidths, ColumnDefinitions, content.Width);
        float[] rowHeights = RedistributeStars(_rowHeights, RowDefinitions, content.Height);

        foreach (UIElement child in Children)
        {
            if (!child.IsVisible) continue;

            Thickness m = child.Margin;

            float cellX = content.X + colWidths.Take(child.Column).Sum();
            float cellY = content.Y + rowHeights.Take(child.Row).Sum();

            child.Arrange(new Rectangle(
                new Point(cellX + m.Left, cellY + m.Top),
                new Size(
                    Math.Max(0, SpanExtent(colWidths, child.Column, child.ColumnSpan) - m.Horizontal),
                    Math.Max(0, SpanExtent(rowHeights, child.Row, child.RowSpan) - m.Vertical))));
        }
    }

    /// <summary>Суммарный размер треков, которые занимает элемент.</summary>
    private static float SpanExtent(float[] tracks, int start, int span)
    {
        if (tracks.Length == 0) return 0;

        start = Math.Clamp(start, 0, tracks.Length - 1);
        int count = Math.Clamp(span, 1, tracks.Length - start);

        float total = 0;

        for (int i = start; i < start + count; i++)
            total += tracks[i];

        return total;
    }

    private float[] ResolveTracks(List<GridLength> defs, float total, bool horizontal)
    {
        float[] sizes = new float[defs.Count];

        // 1. фиксированные — известны сразу
        for (int i = 0; i < defs.Count; i++)
            if (defs[i].Unit == GridUnit.Fixed)
                sizes[i] = defs[i].Value;

        // 2. Auto — по самому крупному ребёнку в треке; для этого детей
        //    надо предварительно измерить без ограничения по этой оси
        bool hasAuto = defs.Any(d => d.IsAuto);

        if (hasAuto)
        {
            foreach (UIElement child in Children)
            {
                if (!child.IsVisible) continue;

                int track = horizontal ? child.Column : child.Row;
                int span = Math.Max(1, horizontal ? child.ColumnSpan : child.RowSpan);

                if (track < 0 || track >= defs.Count || !defs[track].IsAuto) continue;

                child.Measure(horizontal
                    ? new Size(float.PositiveInfinity, total)
                    : new Size(total, float.PositiveInfinity));

                float desired = horizontal
                    ? child.DesiredSize.Width + child.Margin.Horizontal
                    : child.DesiredSize.Height + child.Margin.Vertical;

                // растянутый на несколько треков элемент делит свои запросы
                // между ними, иначе первый трек станет шириной всей кнопки «=»
                sizes[track] = Math.Max(sizes[track], desired / span);
            }
        }

        // 3. Star — делят то, что осталось после Fixed и Auto
        float used = 0;
        for (int i = 0; i < defs.Count; i++)
            if (!defs[i].IsStar)
                used += sizes[i];

        float starSum = defs.Where(d => d.IsStar).Sum(d => d.Value);
        float remaining = Math.Max(0, total - used);

        for (int i = 0; i < defs.Count; i++)
            if (defs[i].IsStar)
                sizes[i] = starSum > 0 ? remaining * (defs[i].Value / starSum) : 0;

        return sizes;
    }

    private static float[] RedistributeStars(float[] measured, List<GridLength> defs, float total)
    {
        if (measured.Length != defs.Count)
            return measured;

        float[] sizes = (float[])measured.Clone();

        float used = 0;
        for (int i = 0; i < defs.Count; i++)
            if (!defs[i].IsStar)
                used += sizes[i];

        float starSum = defs.Where(d => d.IsStar).Sum(d => d.Value);
        float remaining = Math.Max(0, total - used);

        for (int i = 0; i < defs.Count; i++)
            if (defs[i].IsStar)
                sizes[i] = starSum > 0 ? remaining * (defs[i].Value / starSum) : 0;

        return sizes;
    }
}

public enum GridUnit { Fixed, Star, Auto }
