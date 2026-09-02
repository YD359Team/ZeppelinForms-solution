using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Charts;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Layout;

namespace ZF_Linux;

internal class MainForm : Form
{
    public MainForm()
    {
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        this.Title = "Form 1";
        this.Size = new Size(1024, 768);
        this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        this.Content = GetView1();
    }

    private UIElement GetView1()
    {
        DockPanel dockPanel = new DockPanel();
        Button btnNext = new Button() { Text = "Goto view 2", Docking = Dock.Top };
        btnNext.Click -= BtnNext_Click;
        btnNext.Click += BtnNext_Click;
        Label lbl = new Label();
        lbl.Text = "Presentation";
        dockPanel.Children.AddRange([lbl, btnNext]);
        return dockPanel;
    }

    private UIElement GetView2()
    {
        DockPanel dockPanel = new DockPanel();
        Button btnNext = new Button() { Text = "Goto view 1", Docking = Dock.Top };
        btnNext.Click -= BtnBack_Click;
        btnNext.Click += BtnBack_Click;
        var grid = new UniformGrid
        {
            Padding = new Thickness(6),
            OverflowY = Overflow.Auto,        // прокрутка там, где нужна
        };
        PictureBox pBox = new() { Size = new(100, 100) };
        pBox.LoadAsset("Laughing.png");
        ListBox lBox = new();
        lBox.Items.AddRange([new Button() { Text = "Item1" }, new Button() { Text = "Item2" }]);
        ComboBox cBox = new();
        cBox.Items.AddRange("Item 1", "Item 2", "Item 3");
        UIElement[] controls = [
            new Label() { Text = "Label" },
            new Button() { Text = "Button" },
            new Button() { Text = "Button with shadow", BoxShadow = BoxShadow.Large },
            new NumericUpDown(),
            new ProgressBar() { Maximum = 1f, Value = 0.5f },
            new CheckBox() { Text = "CheckBox" },
            new RadioButton() { Text = "RadioButton" },
            new TextBox() { Text = "TextBox" },
            new TrackBar(),
            new DateTimePicker(),
            new Calendar(),
            new TimePicker(),
            pBox,
            lBox,
            cBox,
            new Spoiler() { Child = new Label() { Text = "Hidden label" }, IsCollapsed = false },
            new CircularProgressBar() { Maximum = 1f, Value = 0.5f },
            new SvgIcon() { PathData = "M 45 45 L 345 45 L 345 345 L 45 345 Z M 195 45 L 195 345 M 45 195 L 345 195" },
            new ColorPicker(),
            new SplitButton() { Text = "SplitButton", Items = [ new() { Text = "Item 1" }, new() { Text = "Item 2" }] },
            new ToggleButton() { Text = "ToggleButton" },
            new ToggleSwitch() { Text = "ToggleSwitch" },
            .. GetPlotControls()
        ];
        grid.Children.AddRange(controls);
        dockPanel.Children.AddRange([grid, btnNext]);
        return dockPanel;
    }

    private void BtnBack_Click(object? sender, ZeppelinForms.Input.Mouse.MouseClickEventArgs e)
    {
        this.Content = GetView1();
    }

    private void BtnNext_Click(object? sender, ZeppelinForms.Input.Mouse.MouseClickEventArgs e)
    {
        this.Content = GetView2();
    }

    private UIElement[] GetPlotControls()
    {
        PieChart pieChart = new()
        {
            HoleRatio = 0.5f,
        };
        pieChart.Slices.AddRange(
            new PieSlice() { Color = Colors.Red, Value = 0.25f },
            new PieSlice() { Color = Colors.Blue, Value = 0.75f }
        );
        LineChart lineChart = new()
        {
            Title = "y = sin(x) · x",
            Function = x => MathF.Sin(x) * x,
            FunctionMinX = -10,
            FunctionMaxX = 10,
        };
        BarChart barChart = new()
        {
            Title = "Продажи по кварталам",
            Categories = { "Q1", "Q2", "Q3", "Q4" },
            Series = { new ChartSeries { Values = { 120, 180, 90, 210 } } },
        };

        return [pieChart, lineChart, barChart];
    }
}
