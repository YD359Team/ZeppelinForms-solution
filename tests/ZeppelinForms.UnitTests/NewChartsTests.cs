using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Charts;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class NewChartsTests
{
    private static Form Show(UIElement control, Size size)
    {
        var platform = new HeadlessPlatform();

        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.VerticalAlignment = VerticalAlignment.Stretch;

        var form = new Form { Size = size, Content = control };
        platform.CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    private static void Render(Form form) =>
        ElementTreeRenderer.Draw(form.Content!, new HeadlessGraphics());

    [Fact]
    public void RadarChartDrawsWithData()
    {
        var chart = new RadarChart
        {
            Title = "Навыки",
            Categories = { "Скорость", "Память", "Сеть", "Диск", "Экран" },
            Series =
            {
                new ChartSeries { Name = "было", Values = { 3, 5, 2, 4, 1 } },
                new ChartSeries { Name = "стало", Values = { 5, 4, 4, 2, 3 } },
            },
        };

        Form form = Show(chart, new Size(420, 320));

        Render(form);

        Assert.True(chart.ActualSize.Width > 0);
    }

    [Fact]
    public void RadarChartSurvivesShortSeries()
    {
        // a series shorter than the list of axes — the missing values count as zero
        // rather than crash the drawing
        var chart = new RadarChart
        {
            Categories = { "A", "B", "C", "D" },
            Series = { new ChartSeries { Values = { 1, 2 } } },
        };

        Form form = Show(chart, new Size(300, 300));

        Render(form);
    }

    [Fact]
    public void RadarChartIgnoresTooFewAxes()
    {
        // two axes make no radar: only the background and the title are drawn
        var chart = new RadarChart
        {
            Categories = { "A", "B" },
            Series = { new ChartSeries { Values = { 1, 2 } } },
        };

        Form form = Show(chart, new Size(300, 300));

        Render(form);
    }

    private static CandlestickChart CreateCandles() => new()
    {
        Title = "Котировки",
        Candles =
        {
            new Candle { Label = "пн", Open = 100, High = 105, Low = 99, Close = 104 },
            new Candle { Label = "вт", Open = 104, High = 106, Low = 101, Close = 102 },
            new Candle { Label = "ср", Open = 102, High = 108, Low = 102, Close = 107 },
            new Candle { Label = "чт", Open = 107, High = 107, Low = 103, Close = 103 },
        },
    };

    [Fact]
    public void CandlestickChartDrawsWithData()
    {
        Form form = Show(CreateCandles(), new Size(480, 300));

        Render(form);
    }

    [Fact]
    public void CandlestickHoverHighlightsCandle()
    {
        CandlestickChart chart = CreateCandles();
        Form form = Show(chart, new Size(480, 300));

        Render(form);

        // move the mouse across the plot: the highlight and the price readout must not crash
        HeadlessInput.MoveMouse(form, 300, 150);
        Render(form);

        HeadlessInput.MoveMouse(form, 5, 5);
        Render(form);
    }

    // ===== 0.19.2: hover and readout on every chart =====

    private static LineChart CreateLines() => new()
    {
        Categories = ["Mon", "Tue", "Wed", "Thu", "Fri"],
        Series =
        [
            new ChartSeries { Name = "Visits", Values = [3, 5, 4, 7, 6] },
            new ChartSeries { Name = "Orders", Values = [1, 2, 2, 3] },
        ],
    };

    [Fact]
    public void ALineChartHoversTheNearestCategory()
    {
        LineChart chart = CreateLines();
        Form form = Show(chart, new Size(480, 300));

        // across the plot at its middle height: every category in turn, left to right
        var seen = new List<int>();

        for (float x = 0; x < 480; x += 4)
        {
            HeadlessInput.MoveMouse(form, x, 150);
            Render(form);

            if (chart.HoveredCategory >= 0 && (seen.Count == 0 || seen[^1] != chart.HoveredCategory))
                seen.Add(chart.HoveredCategory);
        }

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, seen);

        // the value axis is not the plot; a series shorter than the categories is no crash
        HeadlessInput.MoveMouse(form, 2, 150);
        Assert.Equal(-1, chart.HoveredCategory);
    }

    [Fact]
    public void AFunctionIsReadAtThePointersArgument()
    {
        var chart = new LineChart { Function = x => x * x, FunctionMinX = -4, FunctionMaxX = 4 };
        Form form = Show(chart, new Size(480, 300));

        float? first = null, last = null;

        for (float x = 0; x < 480; x += 4)
        {
            HeadlessInput.MoveMouse(form, x, 150);
            Render(form);

            if (float.IsNaN(chart.HoveredArgument)) continue;

            first ??= chart.HoveredArgument;
            last = chart.HoveredArgument;
        }

        // the argument runs over the function's range as the pointer crosses the plot
        Assert.True(first is < -3.5f, $"first {first}");
        Assert.True(last is > 3.5f, $"last {last}");

        HeadlessInput.MoveMouse(form, 2, 2);
        Assert.True(float.IsNaN(chart.HoveredArgument));
    }

    [Fact]
    public void ABarChartHoversTheGroupAndTheBar()
    {
        var chart = new BarChart
        {
            Categories = ["Q1", "Q2", "Q3"],
            Series =
            [
                new ChartSeries { Name = "Plan", Values = [10, 10, 10] },
                new ChartSeries { Name = "Fact", Values = [10, 10, 10] },
            ],
        };

        Form form = Show(chart, new Size(480, 300));

        var categories = new List<int>();
        var series = new HashSet<int>();

        // low in the plot, where every bar reaches: the groups in turn, and in them the bars
        for (float x = 0; x < 480; x += 2)
        {
            HeadlessInput.MoveMouse(form, x, 240);
            Render(form);

            if (chart.HoveredCategory >= 0 && (categories.Count == 0 || categories[^1] != chart.HoveredCategory))
                categories.Add(chart.HoveredCategory);

            if (chart.HoveredSeries >= 0) series.Add(chart.HoveredSeries);
        }

        Assert.Equal(new[] { 0, 1, 2 }, categories);
        Assert.Equal(new[] { 0, 1 }, series.Order());
    }

    [Fact]
    public void ARadarChartHoversTheRayNearestByAngle()
    {
        var chart = new RadarChart
        {
            ShowLegend = false,
            Categories = ["Speed", "Power", "Range", "Comfort"],
            Series = [new ChartSeries { Name = "A", Values = [4, 3, 5, 2] }],
        };

        Form form = Show(chart, new Size(400, 400));

        // the rays: up, right, down, left — clockwise from 12 o'clock
        HeadlessInput.MoveMouse(form, 200, 110);
        Render(form);
        Assert.Equal(0, chart.HoveredAxis);

        HeadlessInput.MoveMouse(form, 290, 205);
        Render(form);
        Assert.Equal(1, chart.HoveredAxis);

        HeadlessInput.MoveMouse(form, 110, 195);
        Render(form);
        Assert.Equal(3, chart.HoveredAxis);

        // past the disc and its labels
        HeadlessInput.MoveMouse(form, 3, 3);
        Assert.Equal(-1, chart.HoveredAxis);
    }

    [Fact]
    public void TheHoverColorsAreEveryCharts()
    {
        // 0.19.2: HoverColor and ReadoutBackground moved from the candlestick chart to
        // ChartBase; the theme gives them to every chart
        var candles = CreateCandles();
        var bars = new BarChart();
        Show(new StackPanel { Children = { candles, bars } }, new Size(400, 400));

        Assert.Equal(candles.HoverColor, bars.HoverColor);
        Assert.Equal(candles.ReadoutBackground, bars.ReadoutBackground);
        Assert.NotEqual(0, bars.ReadoutBackground.A);
    }

    [Fact]
    public void CandlestickChartKeepsEmptyStateSafe()
    {
        Form form = Show(new CandlestickChart(), new Size(300, 200));

        Render(form);
    }

    [Fact]
    public void HalfCircleProgressIsWiderThanTall()
    {
        var gauge = new CircularProgressBar
        {
            StartAngle = 180f,
            SweepAngle = 180f,
            Value = 40f,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var platform = new HeadlessPlatform();
        var form = new Form { Size = new Size(400, 300), Content = gauge };
        platform.CreateWindow(form);
        form.UpdateLayout();

        // a half circle takes half as much height as width
        Assert.True(gauge.DesiredSize.Width > gauge.DesiredSize.Height);

        Render(form);
    }

    [Fact]
    public void FullCircleProgressStaysSquare()
    {
        var progress = new CircularProgressBar
        {
            Value = 40f,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var platform = new HeadlessPlatform();
        var form = new Form { Size = new Size(400, 300), Content = progress };
        platform.CreateWindow(form);
        form.UpdateLayout();

        Assert.Equal(progress.DesiredSize.Width, progress.DesiredSize.Height);
    }
}