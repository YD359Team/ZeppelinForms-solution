using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Charts;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>Charts: lines, bars, a pie, a radar and candles.</summary>
internal static class ChartsPage
{
    public static UIElement Create()
    {
        var line = new LineChart
        {
            Title = "Temperature, °C",
            Categories = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" },
            Series =
            {
                new ChartSeries { Name = "Moscow", Values = { -6, -6, -1, 7, 14, 18, 20, 18, 12, 6, -1, -5 } },
                new ChartSeries { Name = "Lisbon", Values = { 11, 12, 14, 16, 18, 21, 23, 24, 22, 19, 15, 12 } },
            },
            ShowPoints = true,
        };

        var function = new LineChart
        {
            Title = "y = sin(x) · x",
            Function = x => MathF.Sin(x) * x,
            FunctionMinX = -10,
            FunctionMaxX = 10,
        };

        var bars = new BarChart
        {
            Title = "Sales by quarter",
            Categories = { "Q1", "Q2", "Q3", "Q4" },
            Series =
            {
                new ChartSeries { Name = "2025", Values = { 120, 180, 90, 210 } },
                new ChartSeries { Name = "2026", Values = { 140, 170, 130, 240 } },
            },
            BarCornerRadius = new(3),
        };

        var pie = new PieChart
        {
            Title = "Platforms",
            HoleRatio = 0.5f,
            ShowLegend = true,
            ShowPercentages = true,
            Slices =
            {
                new PieSlice { Label = "Windows", Value = 46 },
                new PieSlice { Label = "Linux", Value = 21 },
                new PieSlice { Label = "Android", Value = 19 },
                new PieSlice { Label = "Browser", Value = 14 },
            },
        };

        var radar = new RadarChart
        {
            Title = "Build profile",
            ShowLegend = true,
            Categories = { "Speed", "Memory", "Net", "Disk", "Screen", "Battery" },
            Series =
            {
                new ChartSeries { Name = "0.14", Values = { 3, 5, 2, 4, 1, 4 } },
                new ChartSeries { Name = "0.15", Values = { 5, 4, 4, 2, 3, 5 } },
            },
        };

        var candles = new CandlestickChart
        {
            Title = "Quotes",
            HollowBullish = true,
            ShowReadout = true,
            Candles =
            {
                new Candle { Label = "Mon", Open = 100, High = 105, Low = 99, Close = 104 },
                new Candle { Label = "Tue", Open = 104, High = 106, Low = 101, Close = 102 },
                new Candle { Label = "Wed", Open = 102, High = 108, Low = 102, Close = 107 },
                new Candle { Label = "Thu", Open = 107, High = 107, Low = 103, Close = 103 },
                new Candle { Label = "Fri", Open = 103, High = 110, Low = 103, Close = 109 },
            },
        };

        var cells = new UniformGrid { SpacingX = 16, SpacingY = 16 };

        foreach (ChartBase chart in (ChartBase[])[line, function, bars, pie, radar, candles])
        {
            chart.Size = new(float.NaN, 260);
            cells.Children.Add(chart);
        }

        // charts take the size they are given: two per row on a wide screen, one on
        // a narrow. The same grid each time, so the charts keep their hover and state
        var grid = new AdaptiveLayout
        {
            Content = sizeClass =>
            {
                cells.Columns = sizeClass == SizeClass.Compact ? 1 : 2;
                return cells;
            },
        };

        return Demo.Page("Charts", "Data drawn as lines, bars, slices, a radar and candles. Hover them: values show up.",
            Demo.Section("Gallery of charts", grid, "Series without a color take the theme's palette; a function is sampled across its range"));
    }
}
