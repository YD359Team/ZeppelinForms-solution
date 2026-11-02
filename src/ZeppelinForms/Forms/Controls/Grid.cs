using System.Collections.Specialized;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

public class Grid : DecoratedPanel
{
    private float[] _rowHeights = [];
    private float[] _colWidths = [];

    // new track definitions mean a new layout: without Invalidate a grid
    // that was already shown kept its old tracks until something else moved.
    // Since 0.20 the definitions say so themselves, see OnDefinitionsChanged
    public string Columns
    {
        set => ColumnDefinitions.ReplaceAll(GridLength.Parse(value));
    }

    public string Rows
    {
        set => RowDefinitions.ReplaceAll(GridLength.Parse(value));
    }

    /// <summary>The rows' heights. A track added, removed or changed lays the grid out
    /// again: since 0.20 a <see cref="ZfCollection{T}"/>, it was a list only
    /// <see cref="Rows"/> could replace.</summary>
    public ZfCollection<GridLength> RowDefinitions { get; } = [];

    /// <summary>The columns' widths, as <see cref="RowDefinitions"/> are the rows'.</summary>
    public ZfCollection<GridLength> ColumnDefinitions { get; } = [];

    public Grid()
    {
        RowDefinitions.CollectionChanged += OnDefinitionsChanged;
        ColumnDefinitions.CollectionChanged += OnDefinitionsChanged;
    }

    private void OnDefinitionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Invalidate();

    /// <summary>One star track for an axis with none defined, as in WPF: a grid
    /// without Rows or Columns is one cell, its children stacked in it. Without it
    /// every child got a cell of zero and nothing showed — a picture with shapes laid
    /// over it in a grid of no definitions.</summary>
    private static readonly List<GridLength> SingleTrack = [GridLength.Star()];

    private static IReadOnlyList<GridLength> Tracks(ZfCollection<GridLength> definitions) =>
        definitions.Count > 0 ? definitions : SingleTrack;

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var content = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        _colWidths = ResolveTracks(Tracks(ColumnDefinitions), content.Width, horizontal: true);
        _rowHeights = ResolveTracks(Tracks(RowDefinitions), content.Height, horizontal: false);

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

    protected override void ArrangeContentOverride(Size contentSize)
    {
        var content = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, contentSize.Width - Padding.Horizontal),
                Math.Max(0, contentSize.Height - Padding.Vertical)));

        float[] colWidths = RedistributeStars(_colWidths, Tracks(ColumnDefinitions), content.Width);
        float[] rowHeights = RedistributeStars(_rowHeights, Tracks(RowDefinitions), content.Height);

        // where each track starts, summed once: the start of a child's cell used to be
        // summed again for every child, through LINQ — a form of a few hundred fields in a
        // grid of a dozen rows paid for it on every arrange
        float[] colStarts = Starts(colWidths);
        float[] rowStarts = Starts(rowHeights);

        foreach (UIElement child in Children)
        {
            if (!child.IsVisible) continue;

            Thickness m = child.Margin;

            float cellX = content.X + StartOf(colStarts, child.Column);
            float cellY = content.Y + StartOf(rowStarts, child.Row);

            child.Arrange(new Rectangle(
                new Point(cellX + m.Left, cellY + m.Top),
                new Size(
                    Math.Max(0, SpanExtent(colWidths, child.Column, child.ColumnSpan) - m.Horizontal),
                    Math.Max(0, SpanExtent(rowHeights, child.Row, child.RowSpan) - m.Vertical))));
        }
    }

    /// <summary>The offset of every track from the first: one more than there are tracks,
    /// the last being their total.</summary>
    private static float[] Starts(float[] tracks)
    {
        float[] starts = new float[tracks.Length + 1];

        for (int i = 0; i < tracks.Length; i++)
            starts[i + 1] = starts[i] + tracks[i];

        return starts;
    }

    /// <summary>Where the track of an index starts. An index past the tracks is the last
    /// track, as <see cref="SpanExtent"/> takes it: a cell placed past the right edge got
    /// the last column's width at a start past all of them.</summary>
    private static float StartOf(float[] starts, int index) =>
        starts[Math.Clamp(index, 0, Math.Max(0, starts.Length - 2))];

    /// <summary>The total size of the tracks the element occupies.</summary>
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

    private float[] ResolveTracks(IReadOnlyList<GridLength> defs, float total, bool horizontal)
    {
        float[] sizes = new float[defs.Count];

        // 1. fixed — known right away
        for (int i = 0; i < defs.Count; i++)
            if (defs[i].Unit == GridUnit.Fixed)
                sizes[i] = defs[i].Value;

        // 2. Auto — by the largest child in the track; for that the children
        //    must be measured beforehand without a constraint along this axis
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

                // an element spanning several tracks splits its request between them,
                // otherwise the first track would become as wide as the whole "=" button
                sizes[track] = Math.Max(sizes[track], desired / span);
            }
        }

        // 3. Star — share what is left after Fixed and Auto
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

    private static float[] RedistributeStars(float[] measured, IReadOnlyList<GridLength> defs, float total)
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