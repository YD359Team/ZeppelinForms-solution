using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Charts;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing;
using ZeppelinForms.Forms.Controls.Shapes;
using ZeppelinForms.Forms.Controls.Map;
using ZeppelinForms.Forms.Controls.Navigation;

namespace ZF_SharedLib;

public class ExampleMainForm : Form
{
    public ExampleMainForm()
    {
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        this.Title = "Form 1";
        this.Size = new Size(1024, 768);
        this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        this.Content = GetView();
    }

    private UIElement GetView()
    {
        PageControl root = new();
        root.AddPage("home", () => GetView1(), "Home");
        root.AddPage("controls", () => GetView2(), "Controls");
        root.AddPage("calc", () => GetView3(), "Calc");
        return new DockPanel
        {
            Children =
            {
                new Border
                {
                    Docking = Dock.Bottom,
                    Padding = new Thickness(0, 6),
                    Child = root.CreateIndicator(),
                },
                root
            },
        };
    }

    private UIElement GetView1()
    {
        RichLabel lbl = new();
        lbl.SetText("Hi! Welcome to presentation of ", TextRun.Colored("Zeppelin Forms", Colors.Blue), " framework");
        PrimaryButton btn = new()
        {
            Text = "Goto project GitHub",
            Font = new Font("Segoe UI", 16f),
            Size = new(200, 80)
        };
        StackPanel stackPanel = new()
        {
            Orientation = Orientation.Vertical,
            MainAxisAlignment = MainAxisAlignment.Center,
            Spacing = 6,
        };
        stackPanel.Children.AddRange([lbl, btn]);
        return stackPanel;
    }

    private UIElement GetView2()
    {
        var grid = new UniformGrid
        {
            Padding = new Thickness(6),
            OverflowY = Overflow.Auto,
            SpacingX = 5,
            SpacingY = 2,
        };
        PictureBox pBox = new() { Size = new(100, 100) };
        pBox.LoadAsset("Laughing.png");
        ListBox lBox = new();
        lBox.Items.AddRange([new Button() { Text = "Item1" }, new Button() { Text = "Item2" }]);
        ComboBox cBox = new();
        cBox.Items.AddRange("Item 1", "Item 2", "Item 3");
        var map = new MapControl()
        {
            RowSpan = 2,
            ColumnSpan = 2,
            UserAgent = "ZeppelinForms/0.5.0",
        };
        UIElement[] controls = [
            new Label() { Text = "Label" },
            new LinkLabel() { Text = "LinkLabel" },
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
            map,
            new Spoiler() { Child = new Label() { Text = "Hidden label" }, IsCollapsed = true },
            new CircularProgressBar() { Maximum = 1f, Value = 0.5f },
            new SvgIcon() { PathData = "M 45 45 L 345 45 L 345 345 L 45 345 Z M 195 45 L 195 345 M 45 195 L 345 195" },
            new ColorPicker(),
            new SplitButton() { Text = "SplitButton", Items = [ new() { Text = "Item 1" }, new() { Text = "Item 2" }] },
            new ToggleButton() { Text = "ToggleButton" },
            new ToggleSwitch() { Text = "ToggleSwitch" },
            new LineShape() { Stroke = Colors.Black },
            new RectangleShape() { Stroke = Colors.Black },
            new EllipseShape() { Stroke = Colors.Black },
            new PolygonShape() { Stroke = Colors.Black, Points = [new(), new(1, 1), new(0, 1)] },
            .. GetPlotControls()
        ];
        grid.Children.AddRange(controls);
        map.GoTo(55.751244, 37.618423, zoom: 12);
        return grid;
    }

    private UIElement GetView3()
    {
        Grid grid = new()
        {
            Columns = "Auto,Auto,Auto,Auto",
            Rows = "Auto,Auto,Auto,Auto,Auto,Auto"
        };

        TextBox display = new()
        {
            Row = 0,
            Column = 0,
            ColumnSpan = 4
        };

        Button btn7 = new() { Text = "7", Row = 1, Column = 0 };
        Button btn8 = new() { Text = "8", Row = 1, Column = 1 };
        Button btn9 = new() { Text = "9", Row = 1, Column = 2 };
        Button btnAdd = new() { Text = "+", Row = 1, Column = 3 };

        Button btn4 = new() { Text = "4", Row = 2, Column = 0 };
        Button btn5 = new() { Text = "5", Row = 2, Column = 1 };
        Button btn6 = new() { Text = "6", Row = 2, Column = 2 };
        Button btnSub = new() { Text = "-", Row = 2, Column = 3 };

        Button btn1 = new() { Text = "1", Row = 3, Column = 0 };
        Button btn2 = new() { Text = "2", Row = 3, Column = 1 };
        Button btn3 = new() { Text = "3", Row = 3, Column = 2 };
        Button btnMul = new() { Text = "*", Row = 3, Column = 3 };

        Button btnClear = new() { Text = "C", Row = 4, Column = 0 };
        Button btn0 = new() { Text = "0", Row = 4, Column = 1 };
        Button btnDot = new() { Text = ".", Row = 4, Column = 2 };
        Button btnDiv = new() { Text = "/", Row = 4, Column = 3 };

        Button btnEquals = new()
        {
            Text = "=",
            Row = 5,
            Column = 0,
            ColumnSpan = 4
        };

        grid.Children.AddRange([
            display,
        btn7, btn8, btn9, btnAdd,
        btn4, btn5, btn6, btnSub,
        btn1, btn2, btn3, btnMul,
        btnClear, btn0, btnDot, btnDiv,
        btnEquals
        ]);

        return grid;
    }

    private UIElement[] GetPlotControls()
    {
        PieChart pieChart = new()
        {
            HoleRatio = 0.5f,
        };
        pieChart.Slices.AddRange(
            new PieSlice() { Color = Colors.Red, Value = 0.25f, Label = "Red" },
            new PieSlice() { Color = Colors.Blue, Value = 0.75f, Label = "Blue" }
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