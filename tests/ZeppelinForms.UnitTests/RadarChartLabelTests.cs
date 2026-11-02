using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Charts;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.21: a radar chart's axis labels stand wholly outside its disc — a long
/// label on a side ray used to run over the grid and the series — and inside the control.</summary>
[Collection("Platform")]
public sealed class RadarChartLabelTests
{
    private sealed class Recorder : HeadlessGraphics
    {
        public List<(string Text, Rectangle Box)> Texts { get; } = [];

        public List<(Point From, Point To, Color Color)> Lines { get; } = [];

        public override void DrawText(string text, Rectangle rect, Color color, Font font,
            HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
            VerticalContentAlignment vAlign = VerticalContentAlignment.Center) => Texts.Add((text, rect));

        public override void DrawLine(Point from, Point to, Color color, float width) => Lines.Add((from, to, color));
    }

    private static readonly string[] Categories =
        ["Speed", "A rather long category", "Memory", "Disk throughput under load", "Screen"];

    private static (RadarChart Chart, Recorder Drawn) Draw(Size size)
    {
        var chart = new RadarChart
        {
            ShowLegend = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Series = { new ChartSeries { Values = { 5, 4, 3, 5, 2 } } },
        };

        foreach (string category in Categories)
            chart.Categories.Add(category);

        var form = new Form { Size = size, Content = chart };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        var drawn = new Recorder();
        ElementTreeRenderer.Draw(chart, drawn);

        return (chart, drawn);
    }

    /// <summary>The distance from a point to the nearest point of a box.</summary>
    private static float Distance(Point point, Rectangle box)
    {
        float dx = Math.Max(Math.Max(box.X - point.X, 0f), point.X - (box.X + box.Width));
        float dy = Math.Max(Math.Max(box.Y - point.Y, 0f), point.Y - (box.Y + box.Height));

        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>A category's label as drawn: whole, or its beginning with an ellipsis.</summary>
    private static bool IsLabel(string text) =>
        Categories.Contains(text) ||
        (text.EndsWith('…') && Categories.Any(c => c.StartsWith(text.TrimEnd('…'), StringComparison.Ordinal)));

    [Theory]
    [InlineData(420, 300)]
    [InlineData(300, 420)]
    [InlineData(600, 220)]
    [InlineData(200, 160)]
    public void NoLabelStandsOverTheDisc(int width, int height)
    {
        (RadarChart chart, Recorder drawn) = Draw(new Size(width, height));

        // the axes go from the center to the disc's edge
        List<(Point From, Point To, Color Color)> axes = [.. drawn.Lines.Where(l => l.Color == chart.AxisColor)];
        Assert.Equal(Categories.Length, axes.Count);

        Point center = axes[0].From;
        float radius = axes.Max(a => MathF.Sqrt(MathF.Pow(a.To.X - center.X, 2) + MathF.Pow(a.To.Y - center.Y, 2)));

        Assert.True(radius > 0);

        foreach ((string text, Rectangle box) in drawn.Texts.Where(t => IsLabel(t.Text)))
        {
            Assert.True(Distance(center, box) >= radius, $"'{text}' stands over the disc");

            // and inside the control
            Assert.True(box.X >= -0.5f && box.X + box.Width <= chart.ActualSize.Width + 0.5f, $"'{text}' is cut");
            Assert.True(box.Y >= -0.5f && box.Y + box.Height <= chart.ActualSize.Height + 0.5f, $"'{text}' is cut");
        }
    }

    [Fact]
    public void InASmallChartTheDiscKeepsItsShareAndLongLabelsAreShortened()
    {
        (RadarChart chart, Recorder drawn) = Draw(new Size(200, 160));

        List<(Point From, Point To, Color Color)> axes = [.. drawn.Lines.Where(l => l.Color == chart.AxisColor)];
        Point center = axes[0].From;
        float radius = axes.Max(a => MathF.Sqrt(MathF.Pow(a.To.X - center.X, 2) + MathF.Pow(a.To.Y - center.Y, 2)));

        // the labels as long as they are would leave no disc at all
        Assert.True(radius >= Math.Min(chart.ActualSize.Width, chart.ActualSize.Height) / 2f * 0.3f - 1f);
        Assert.Contains(drawn.Texts, t => t.Text.EndsWith('…'));
        Assert.Contains(drawn.Texts, t => t.Text == "Speed");
    }

    [Fact]
    public void TheOldPlacementIsKeptWhenAsked()
    {
        var chart = new RadarChart { KeepLabelsClear = false, ShowLegend = false };

        foreach (string category in Categories)
            chart.Categories.Add(category);

        var form = new Form { Size = new Size(420, 300), Content = chart };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        var drawn = new Recorder();
        ElementTreeRenderer.Draw(chart, drawn);

        Assert.Equal(Categories.Length, drawn.Texts.Count(t => Categories.Contains(t.Text)));
    }
}
