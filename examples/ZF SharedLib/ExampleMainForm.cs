using System.Diagnostics;
using System.Runtime.InteropServices;
using ZeppelinForms;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Effects;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Charts;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Controls.Map;
using ZeppelinForms.Forms.Controls.Navigation;
using ZeppelinForms.Forms.Controls.Shapes;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.DragDrop;

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
        this.AllowDrop = true;

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
        root.AddPage("loader", () => GetView6(), "Loader");
        root.AddPage("dnd", () => GetView7(), "Drag&Drop");
        root.AddPage("table", () => GetView8(), "Table");
        root.AddPage("sysdnd", () => GetView9(), "System Drag&Drop");
        return new DockPanel
        {
            Children =
            {
                new Border
                {
                    Docking = Dock.Bottom,
                    Padding = new(0, 6),
                    Child = root.CreateIndicator(),
                },
                root
            },
        };
    }

    private GradientBorder GetView1() =>
      new(new StackPanel
      {
          Orientation = Orientation.Vertical,
          MainAxisAlignment = MainAxisAlignment.Center,
          Spacing = 6,
          Children =
          {
                new PictureBox { Size = new(128, 128), Padding = new(8) }
                    .With(x => { x.SetImage(Assets.LogoImage()); x.Glitch(); }),

                new RichLabel()
                    .With(x => x.SetText(
                        "Hi! Welcome to presentation of ",
                        TextRun.Colored("Zeppelin Forms", Colors.Blue),
                        " framework")),

                new PrimaryButton
                {
                    Text = "Open project on GitHub",
                    Font = Font.Default.WithSize(16f),
                    Size = new(200, 80),
                }
                .With(x => x.Click += (_, _) => OpenGitHub()),
          },
      })
      {
          Stops = [new(MediaColors.DotnetModern, 0), new(MediaColors.Dotnet, 1)],
          BorderWidth = 2f,
      };

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

    private AdaptiveLayout GetView2()
    {
        var adaptiveLayout = new AdaptiveLayout();
        PictureBox pBox = new PictureBox().With(x =>
        {
            x.Size = new(100, 100);
            x.LoadAsset("Laughing.png");
        });
        ListBox lBox = new ListBox().With(x => x.Items.AddRange([new Button() { Text = "Item1" }, new Button() { Text = "Item2" }]));
        ComboBox cBox = new ComboBox().With(x => x.Items.AddRange("Item 1", "Item 2", "Item 3"));
        UIElement[] controls = [
            new Label() { Text = "Label" },
            new LinkLabel() { Text = "LinkLabel" },
            new Button() { Text = "Button" },
            new NumericUpDown(),
            new ProgressBar() { Maximum = 1f, Value = 0.5f },
            new CheckBox() { Text = "CheckBox" },
            new RadioButton() { Text = "RadioButton" },
            new TextBox() { Watermark = "Print text here..." },
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
        adaptiveLayout.Content = size => size == SizeClass.Compact
        ? new StackPanel() 
        { 
            Orientation = Orientation.Vertical, Spacing = 5, OverflowY = Overflow.Auto 
        }.With(x => x.Children.AddRange(controls))
        : new StackPanel() 
        { 
            Orientation = Orientation.Horizontal, Spacing = 3, OverflowY = Overflow.Auto 
        }.With(x => x.Children.AddRange(controls));

        return adaptiveLayout;
    }

    private Grid GetView3()
    {
        Grid grid = new()
        {
            Columns = "*,*,*,*",
            Rows = "Auto,*,*,*,*,*",
            Padding = new(8),
            Font = Font.Default.WithSize(18f),
        };

        TextBox display = new()
        {
            Text = "0",
            IsReadOnly = true,
            HorizontalContentAlign = HorizontalContentAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new(3),
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
                display.Text = "Divide by zero";
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

    private StackPanel GetView6()
    {
        Loader ring = new() { Style = LoaderStyle.Ring };
        Loader spinner = new() { Style = LoaderStyle.Spinner };
        Loader dots = new() { Style = LoaderStyle.Dots, IndicatorSize = 48f };
        Loader bar = new()
        {
            Style = LoaderStyle.Bar,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        Loader[] all = [ring, spinner, dots, bar];

        Button toggle = new()
        {
            Text = "Stop all",
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        toggle.Click += (_, _) =>
        {
            bool running = !ring.IsRunning;

            foreach (Loader loader in all)
                loader.IsRunning = running;

            toggle.Text = running ? "Stop all" : "Start all";
        };

        StackPanel row = new StackPanel()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 32,
            MainAxisAlignment = MainAxisAlignment.Center,
            Margin = new Thickness(0, 24, 0, 24),
        }.With(x => x.Children.AddRange([
            Labelled("Ring", ring),
            Labelled("Spinner", spinner),
            Labelled("Dots", dots),
        ]));

        return new StackPanel()
        {
            Orientation = Orientation.Vertical,
            Spacing = 16,
            Padding = new(16),
        }.With(x => x.Children.AddRange([row, Labelled("Bar", bar), toggle]));

        static UIElement Labelled(string caption, UIElement indicator)
        {
            return new StackPanel()
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                CrossAxisAlignment = CrossAxisAlignment.Center,
            }.With(x => x.Children.AddRange([indicator, new Label { Text = caption }]));
        }
    }

    private MapControl GetView4()
    {
        return new MapControl()
        {
            UserAgent = "ZeppelinForms/0.10.0",
        }.With(x => x.GoTo(55.751244, 37.618423, zoom: 12));
    }

    private Grid GetView5()
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

    private UIElement GetView7()
    {
        Label log = new()
        {
            Text = "Move card from list to neighbor",
            Margin = new(4, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        DragList backlog = Column("tasks");
        backlog.Items.AddRange([
            Card("Migrate Calendar"),
            Card("Write down README"),
            Card("WrapPanel"),
            Card("Table"),
        ]);

        DragList progress = Column("tasks");
        progress.Items.Add(Card("Drag&Drop"));

        // предел незавершённой работы — ровно тот случай,
        // ради которого в DragList есть ReceivePredicate
        progress.ReceivePredicate = (_, _) => progress.Items.Count < 3;

        // из готового обратно не забрать: CanSendItem гасит захват на нажатии
        DragList done = Column("tasks");
        done.CanSendItem = false;
        done.Items.Add(Card("StyledProperty"));

        // другая группа: сюда из "tasks" уронить нельзя, хотя список рядом
        DragList notes = Column("notes");
        notes.Items.AddRange<object>([
            Card("Check XDND"),
            Card("Quested about brushes"),
        ]);

        foreach (DragList list in (DragList[])[backlog, progress, done, notes])
        {
            list.ItemSent += (_, args) =>
                log.Text = ReferenceEquals(args.Source, args.Target)
                    ? $"swapped: {args.SourceIndex} → {args.TargetIndex}"
                    : "card left the list";

            list.ItemReceived += (_, args) => log.Text = $"received at position {args.TargetIndex}";
        }

        Grid grid = new()
        {
            Columns = "*,*,*,*",
            Rows = "Auto,*,Auto",
            Padding = new(8),
        };

        grid.Children.Add(Header("Backlog"), 0, 0);
        grid.Children.Add(Header("In progress (max 3)"), 0, 1);
        grid.Children.Add(Header("Done (only receive)"), 0, 2);
        grid.Children.Add(Header("Notes (other group)"), 0, 3);

        grid.Children.Add(backlog, 1, 0);
        grid.Children.Add(progress, 1, 1);
        grid.Children.Add(done, 1, 2);
        grid.Children.Add(notes, 1, 3);

        log.Row = 2;
        log.ColumnSpan = 4;
        grid.Children.Add(log);

        return grid;

        // карточка сама становится контейнером строки: ItemsControl
        // оборачивает в Label только те данные, что не являются UIElement
        static UIElement Card(string text) => new Border
        {
            CornerRadius = new CornerRadius(4f),
            BorderWidth = 1f,
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new Label { Text = text },
        };

        static DragList Column(string group) => new()
        {
            Group = group,
            Padding = new Thickness(6),
            OverflowY = Overflow.Auto,
            Margin = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        static UIElement Header(string text) =>
            new Label { Text = text, Margin = new Thickness(4) };
    }

    private StackPanel GetView8()
    {
        Table table = new()
        {
            Columns = [ 
                new() { Header = "#"}, 
                new() { Header = "Name" }, 
                new() { Header = "Price" },
                new() { Header = "In stock" },
            ]
        };
        table.AddRow("1", "Apple", "0.5$", "yes");
        table.AddRow("2", "Tomato", "0.35$", "no");
        table.AddRow("3", "Bread", "1$", "no");
        table.AddRow("4", "Juice", "2$", "no");
        table.AddRow("5", "Cola", "1$", "yes");

        DataGridView dataGrid = new DataGridView();

        return new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 5,
            ScrollBarMode = ScrollBarMode.Inline
        }.With(x => x.Children.AddRange([table, dataGrid]));
    }

    private StackPanel GetView9()
    {
        Label hint = new()
        {
            Text = "Drop file for explorer here",
            HorizontalContentAlign = HorizontalContentAlignment.Center,
            VerticalContentAlign = VerticalContentAlignment.Center,
        };

        ListBox dropped = new()
        {
            OverflowY = Overflow.Auto,
            SelectionMode = SelectionMode.Extended,
            Margin = new Thickness(0, 8, 0, 0),
        };

        Border zone = new()
        {
            AllowDrop = true,
            BorderWidth = 2f,
            CornerRadius = new CornerRadius(8f),
            Padding = new Thickness(24),
            Size = new Size(float.NaN, 120),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = hint,
        };

        // цвет рамки в покое запомним: тема задаст его при добавлении в форму,
        // так что снимать значение надо позже, в первом DragEnter
        Color idleBorder = Colors.Transparent;

        zone.DragEnter += (_, args) =>
        {
            if (idleBorder.A == 0) idleBorder = zone.BorderColor;

            // эффект обязателен: без него источник покажет «нельзя»
            // и до Drop дело не дойдёт
            args.Effect = args.Data.HasFiles ? DragDropEffect.Copy : DragDropEffect.None;

            if (args.Effect == DragDropEffect.None) return;

            zone.BorderColor = new Color(255, 0, 120, 215);
            hint.Text = "Release";
        };

        zone.DragLeave += (_, _) =>
        {
            zone.BorderColor = idleBorder;
            hint.Text = "Drop files here!";
        };

        zone.Drop += (_, args) =>
        {
            dropped.Items.Clear();

            foreach (string path in args.Data.Files)
                dropped.Items.Add(path);

            hint.Text = $"Complete: {args.Data.Files.Count}";
        };

        StackPanel root = new()
        {
            Orientation = Orientation.Vertical,
            Padding = new(16),
            Spacing = 8,
        };

        root.Children.AddRange([zone, dropped]);

        return root;
    }

    /// <summary>Матовое стекло. Фон обязан быть прозрачным, иначе
    /// DecoratedPanel зальёт его поверх размытой подложки.</summary>
    private static StackPanel AcrylicCard()
    {
        StackPanel glass = new()
        {
            Background = Colors.Transparent,
            CornerRadius = new(12f),
            Padding = new(16),
            Size = new(260, 96),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4,
        };

        glass.Children.AddRange([
            new Label { Text = "AcrylicEffect" },
            new Label { Text = "blurred backdrop, tint and noise" },
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
    private static Label BlurCard()
    {
        return new Label()
        {
            Text = "BlurEffect",
            Font = Font.Default.WithSize(28f),
            TextColor = Colors.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        }.With(x => x.Effects.Add(new BlurEffect(3f)));
    }

    /// <summary>Отражение уходит вниз, поэтому элемент прижат к верху ячейки —
    /// снизу должно остаться место внутри ContentBounds родителя.</summary>
    private static PictureBox ReflectionCard()
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
    private static Label TransformCard()
    {
        Label label = new()
        {
            Text = "TransformEffect",
            Font = Font.Default.WithSize(20f),
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
        return [
            new PieChart()
            {
                HoleRatio = 0.5f,
            }.With(x => x.Slices.AddRange(
                new PieSlice() { Color = Colors.Red, Value = 0.25f, Label = "Red" },
                new PieSlice() { Color = Colors.Blue, Value = 0.75f, Label = "Blue" }
            )),
            new LineChart()
            {
                Title = "y = sin(x) · x",
                Function = x => MathF.Sin(x) * x,
                FunctionMinX = -10,
                FunctionMaxX = 10,
            },
            new BarChart()
            {
                Title = "Sales by quarter",
                Categories = { "Q1", "Q2", "Q3", "Q4" },
                Series = { new ChartSeries { Values = { 120, 180, 90, 210 } } },
            }
        ];
    }
}