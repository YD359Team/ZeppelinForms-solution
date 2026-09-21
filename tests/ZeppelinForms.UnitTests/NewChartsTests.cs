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
        // ряд короче списка осей — недостающее считается нулём,
        // а не роняет отрисовку
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
        // на двух осях лепестка не бывает: рисуем только фон и заголовок
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

        // проводим мышью по полю: подсветка и подпись цен не должны падать
        HeadlessInput.MoveMouse(form, 300, 150);
        Render(form);

        HeadlessInput.MoveMouse(form, 5, 5);
        Render(form);
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

        // полукруг занимает вдвое меньше высоты, чем ширины
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