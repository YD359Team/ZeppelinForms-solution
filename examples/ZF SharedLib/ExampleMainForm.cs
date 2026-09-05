using System.Diagnostics;
using System.Runtime.InteropServices;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Effects;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Charts;
using ZeppelinForms.Forms.Controls.Map;
using ZeppelinForms.Forms.Controls.Navigation;
using ZeppelinForms.Forms.Controls.Shapes;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

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
        root.AddPage("map", () => GetView4(), "Map");
        root.AddPage("effects", () => GetView5(), "Effects");
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
        btn.Click += (_, _) => OpenGitHub();
        StackPanel stackPanel = new()
        {
            Orientation = Orientation.Vertical,
            MainAxisAlignment = MainAxisAlignment.Center,
            Spacing = 6,
        };
        stackPanel.Children.AddRange([lbl, btn]);
        return stackPanel;
    }

    private void OpenGitHub()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _ = Process.Start(new ProcessStartInfo() 
            {
                FileName = "https://github.com/YD359Team/ZeppelinForms-solution/", 
                UseShellExecute = true 
            });
        }
        else
        {
            _ = Process.Start("https://github.com/YD359Team/ZeppelinForms-solution/");
        }
    }

    private UIElement GetView2()
    {
        var grid = new UniformGrid
        {
            Padding = new Thickness(6),
            OverflowY = Overflow.Auto,
            SpacingX = 5,
            SpacingY = 2,
            ScrollBarMode = ScrollBarMode.Inline
        };
        PictureBox pBox = new() { Size = new(100, 100) };
        pBox.LoadAsset("Laughing.png");
        ListBox lBox = new();
        lBox.Items.AddRange([new Button() { Text = "Item1" }, new Button() { Text = "Item2" }]);
        ComboBox cBox = new();
        cBox.Items.AddRange("Item 1", "Item 2", "Item 3");
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
        return grid;
    }

    private UIElement GetView3()
    {
        Grid grid = new()
        {
            Columns = "*,*,*,*",
            Rows = "Auto,*,*,*,*,*",
            Padding = new Thickness(8),
            Font = new Font("Segoe UI", 18f),
        };

        TextBox display = new()
        {
            Text = "0",
            IsReadOnly = true,
            HorizontalContentAlign = HorizontalContentAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(3),
            ColumnSpan = 4,
        };

        // состояние живёт в замыканиях: страница пересоздаётся при переходе,
        // и калькулятор каждый раз стартует с чистого листа
        double accumulator = 0;
        char pending = '\0';
        bool startNewNumber = true;

        double Current() =>
            double.TryParse(display.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)
                ? value
                : 0;

        void Show(double value) =>
            display.Text = value.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);

        void Reset()
        {
            accumulator = 0;
            pending = '\0';
            startNewNumber = true;
            display.Text = "0";
        }

        void AppendDigit(string digit)
        {
            if (startNewNumber)
            {
                display.Text = digit;
                startNewNumber = false;
                return;
            }

            // единственный ноль заменяем, а не дописываем к нему
            display.Text = display.Text == "0" ? digit : display.Text + digit;
        }

        void AppendDot()
        {
            if (startNewNumber)
            {
                display.Text = "0.";
                startNewNumber = false;
                return;
            }

            if (!display.Text!.Contains('.'))
                display.Text += ".";
        }

        // возвращает false, если операция невозможна — тогда дисплей уже занят ошибкой
        bool ApplyPending()
        {
            if (pending == '\0')
            {
                accumulator = Current();
                return true;
            }

            double right = Current();

            if (pending == '/' && right == 0)
            {
                display.Text = "Деление на ноль";
                pending = '\0';
                accumulator = 0;
                startNewNumber = true;
                return false;
            }

            accumulator = pending switch
            {
                '+' => accumulator + right,
                '-' => accumulator - right,
                '*' => accumulator * right,
                '/' => accumulator / right,
                _ => right,
            };

            Show(accumulator);
            return true;
        }

        void SetOperator(char op)
        {
            // подряд нажатые операции не должны копить вычисления:
            // если число ещё не вводили, просто меняем знак операции
            if (!startNewNumber && !ApplyPending())
                return;

            if (startNewNumber && pending == '\0')
                accumulator = Current();

            pending = op;
            startNewNumber = true;
        }

        void Equals()
        {
            if (!ApplyPending()) return;

            pending = '\0';
            startNewNumber = true;
        }

        Button Key(string text, int row, int column, Action action, int columnSpan = 1)
        {
            Button button = new()
            {
                Text = text,
                Row = row,
                Column = column,
                ColumnSpan = columnSpan,
                Margin = new Thickness(3),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            button.Click += (_, _) => action();

            return button;
        }

        Button Digit(string text, int row, int column) =>
            Key(text, row, column, () => AppendDigit(text));

        Button Operator(string text, int row, int column) =>
            Key(text, row, column, () => SetOperator(text[0]));

        grid.Children.AddRange([
            display,

            Digit("7", 1, 0), Digit("8", 1, 1), Digit("9", 1, 2), Operator("+", 1, 3),
            Digit("4", 2, 0), Digit("5", 2, 1), Digit("6", 2, 2), Operator("-", 2, 3),
            Digit("1", 3, 0), Digit("2", 3, 1), Digit("3", 3, 2), Operator("*", 3, 3),

            Key("C", 4, 0, Reset),
            Digit("0", 4, 1),
            Key(".", 4, 2, AppendDot),
            Operator("/", 4, 3),

            Key("=", 5, 0, Equals, columnSpan: 4),
        ]);

        return grid;
    }

    private UIElement GetView4()
    {
        var map = new MapControl()
        {
            UserAgent = "ZeppelinForms/0.5.0",
        };
        map.GoTo(55.751244, 37.618423, zoom: 12);
        return map;
    }

    private UIElement GetView5()
    {
        // подложка: на одноцветном фоне ни акрил, ни отражение не читаются
        PictureBox backdrop = new()
        {
            RowSpan = 2,
            ColumnSpan = 2,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        backdrop.LoadAsset("Laughing.png");

        Grid grid = new()
        {
            Columns = "*,*",
            Rows = "*,*",
        };

        grid.Children.Add(backdrop);
        grid.Children.Add(AcrylicCard(), 0, 0);
        grid.Children.Add(BlurCard(), 0, 1);
        grid.Children.Add(ReflectionCard(), 1, 0);
        grid.Children.Add(TransformCard(), 1, 1);

        return grid;
    }

    /// <summary>Матовое стекло. Фон обязан быть прозрачным, иначе
    /// DecoratedPanel зальёт его поверх размытой подложки.</summary>
    private static UIElement AcrylicCard()
    {
        StackPanel glass = new()
        {
            Background = Colors.Transparent,
            CornerRadius = new CornerRadius(12f),
            Padding = new Thickness(16),
            Size = new Size(260, 96),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4,
        };

        glass.Children.AddRange([
            new Label { Text = "AcrylicEffect" },
            new Label { Text = "размытая подложка, тон и шум" },
        ]);

        glass.Effects.Add(new AcrylicEffect
        {
            BlurRadius = 24f,
            TintColor = new Color(150, 255, 255, 255),
            NoiseOpacity = 0.05f,
        });

        return glass;
    }

    /// <summary>Размытие самого элемента, а не подложки под ним.</summary>
    private static UIElement BlurCard()
    {
        Label label = new()
        {
            Text = "BlurEffect",
            Font = new Font("Segoe UI", 28f),
            TextColor = Colors.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        label.Effects.Add(new BlurEffect(3f));

        return label;
    }

    /// <summary>Отражение уходит вниз, поэтому элемент прижат к верху ячейки —
    /// снизу должно остаться место внутри ContentBounds родителя.</summary>
    private static UIElement ReflectionCard()
    {
        PictureBox picture = new()
        {
            Size = new Size(120, 120),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 10, 0, 0),
        };

        picture.LoadAsset("Laughing.png");

        picture.Effects.Add(new ReflectionEffect
        {
            Height = 0.45f,
            Gap = 3f,
            StartOpacity = 0.4f,
        });

        return picture;
    }

    /// <summary>Поворот, наклон и масштаб одним эффектом.</summary>
    private static UIElement TransformCard()
    {
        Label label = new()
        {
            Text = "TransformEffect",
            Font = new Font("Segoe UI", 20f),
            TextColor = Colors.White,
            Background = new Color(160, 0, 0, 0),
            Padding = new Thickness(12, 8),
            CornerRadius = new CornerRadius(6f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        label.Effects.Add(new TransformEffect
        {
            Rotation = -12f,
            ScaleX = 1.15f,
            ScaleY = 1.15f,
            SkewX = 0.1f,
        });

        return label;
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