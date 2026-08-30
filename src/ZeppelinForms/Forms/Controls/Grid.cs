using System.Globalization;
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

    protected override Size MeasureOverride(Size availableSize)
    {
        var content = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        _colWidths = ResolveTracks(ColumnDefinitions, content.Width, horizontal: true);
        _rowHeights = ResolveTracks(RowDefinitions, content.Height, horizontal: false);

        // окончательное измерение детей — уже по реальным размерам ячеек
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            var m = child.Margin;

            child.Measure(new Size(
                Math.Max(0, _colWidths.ElementAtOrDefault(child.Column) - m.Horizontal),
                Math.Max(0, _rowHeights.ElementAtOrDefault(child.Row) - m.Vertical)));
        }

        return ResolveSize(
            new Size(_colWidths.Sum() + Padding.Horizontal, _rowHeights.Sum() + Padding.Vertical),
            availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var content = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical)));

        // Auto-треки уже посчитаны в MeasureOverride по желаемым размерам детей;
        // пересчитывать нельзя — Star-доли изменятся и раскладка «прыгнет»
        float[] colWidths = RedistributeStars(_colWidths, ColumnDefinitions, content.Width);
        float[] rowHeights = RedistributeStars(_rowHeights, RowDefinitions, content.Height);

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            var m = child.Margin;

            float cellX = content.X + colWidths.Take(child.Column).Sum();
            float cellY = content.Y + rowHeights.Take(child.Row).Sum();

            child.Arrange(new Rectangle(
                new Point(cellX + m.Left, cellY + m.Top),
                new Size(
                    Math.Max(0, colWidths.ElementAtOrDefault(child.Column) - m.Horizontal),
                    Math.Max(0, rowHeights.ElementAtOrDefault(child.Row) - m.Vertical))));
        }

        return finalSize;
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
            foreach (var child in Children)
            {
                if (!child.IsVisible) continue;

                int track = horizontal ? child.Column : child.Row;
                if (track < 0 || track >= defs.Count || !defs[track].IsAuto) continue;

                child.Measure(horizontal
                    ? new Size(float.PositiveInfinity, total)
                    : new Size(total, float.PositiveInfinity));

                float desired = horizontal
                    ? child.DesiredSize.Width + child.Margin.Horizontal
                    : child.DesiredSize.Height + child.Margin.Vertical;

                sizes[track] = Math.Max(sizes[track], desired);
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

public readonly partial record struct GridLength(float Value, GridUnit Unit)
{
    public bool IsStar => Unit == GridUnit.Star;
    public bool IsAuto => Unit == GridUnit.Auto;

    public static GridLength Fixed(float px) => new(px, GridUnit.Fixed);
    public static GridLength Star(float weight = 1) => new(weight, GridUnit.Star);
    public static GridLength Auto => new(0, GridUnit.Auto);

    private static GridLength ParseSize(ReadOnlySpan<char> chars)
    {
        if (chars.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return Auto;

        if (chars[^1] == '*')
        {
            ReadOnlySpan<char> weight = chars[..^1].Trim();

            if (weight.IsEmpty)
                return Star();

            if (float.TryParse(weight, NumberStyles.Float, CultureInfo.InvariantCulture, out float w))
                return Star(w);

            throw new FormatException($"Не удалось разобрать вес звезды: '{chars}'.");
        }

        if (float.TryParse(chars, NumberStyles.Float, CultureInfo.InvariantCulture, out float px))
            return Fixed(px);

        throw new FormatException($"Не удалось разобрать размер трека: '{chars}'.");
    }
}

public enum GridUnit { Fixed, Star, Auto }
