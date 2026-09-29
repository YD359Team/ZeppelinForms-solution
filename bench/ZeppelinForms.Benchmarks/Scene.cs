using SkiaSharp;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Skia;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Benchmarks;

/// <summary>
/// A scene: a form with a control tree plus an offscreen Skia surface it is drawn
/// into. Holds everything one scenario needs and releases the native surface
/// in Dispose.
/// </summary>
/// <remarks>
/// The scenes' text content — captions, list rows — is kept as it was, in Russian,
/// on purpose: the cost of measuring and drawing text depends on the text itself,
/// and changing it would make the committed baselines incomparable with new runs.
/// </remarks>
public sealed class Scene : IDisposable
{
    public required Form Form { get; init; }
    public required SKSurface Surface { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    public SKCanvas Canvas => Surface.Canvas;

    /// <summary>How many elements are in the tree — printed next to the result,
    /// otherwise the numbers have nothing to be related to.</summary>
    public required int ElementCount { get; init; }

    public void Dispose() => Surface.Dispose();
}

public static class Scenes
{
    public const int DefaultWidth = 1280;
    public const int DefaultHeight = 800;

    private static bool _servicesRegistered;

    /// <summary>
    /// A headless platform with real Skia services. Exactly the same set as in
    /// SnapshotFixture: measuring the Headless stand-ins is pointless —
    /// HeadlessGraphics does nothing.
    /// </summary>
    public static void EnsureServices()
    {
        if (_servicesRegistered) return;

        ZfContract.Behavior = ContractViolationBehavior.Throw;

        SkiaTextMeasurer.Register();
        SkiaImageDecoder.Register();
        SkiaOffscreenRenderer.Register();

        App.Theme = Themes.Light;
        Font.Default = new Font("sans-serif", 14);

        _servicesRegistered = true;
    }

    /// <summary>
    /// A long virtualized list. There are many rows on purpose: the point of the
    /// scenario is that the cost of a frame must not depend on their number.
    /// </summary>
    public static Scene VirtualizedList(
        int rows = 5000,
        int width = DefaultWidth,
        int height = DefaultHeight)
    {
        EnsureServices();

        var list = new VirtualizingStackPanel
        {
            ItemHeight = 28,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        for (int i = 0; i < rows; i++)
            list.ItemsSource.Add($"Строка {i} — данные, подпись, значение");

        var form = new Form
        {
            Size = new Size(width, height),
            Content = list,
        };

        new HeadlessPlatform(registerServices: false).CreateWindow(form);
        form.UpdateLayout();

        return new Scene
        {
            Form = form,
            Surface = CreateSurface(width, height),
            Width = width,
            Height = height,

            // the created containers, not the source rows:
            // those are exactly what a frame measures
            ElementCount = list.Children.Count,
        };
    }

    /// <summary>
    /// A typical business form: a header, a button bar, a grid of fields, a list.
    /// Deliberately without animations and effects — we measure the base cost
    /// of an ordinary window, not the peak one.
    /// </summary>
    public static Scene BusinessForm(
        int rows = 40,
        int width = DefaultWidth,
        int height = DefaultHeight)
    {
        EnsureServices();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Padding = new Thickness(8),
        };

        toolbar.Children.AddRange(
        [
            new Button { Text = "Создать" },
            new Button { Text = "Открыть" },
            new Button { Text = "Сохранить" },
            new Button { Text = "Удалить" },
            new CheckBox { Text = "Только активные" },
        ]);

        var fields = new UniformGrid
        {
            Padding = new Thickness(8),
            SpacingX = 6,
            SpacingY = 4,
            OverflowY = Overflow.Auto,
            ScrollBarMode = ScrollBarMode.Inline,
        };

        for (int i = 0; i < rows; i++)
        {
            fields.Children.Add(new Label { Text = $"Поле {i + 1}" });
            fields.Children.Add(new TextBox { Text = $"Значение {i + 1}" });
            fields.Children.Add(new ProgressBar { Maximum = 1f, Value = (i % 10) / 10f });
        }

        var root = new DockPanel();

        root.Children.Add(new Border
        {
            Docking = Dock.Top,
            Child = toolbar,
        });

        root.Children.Add(fields);

        var form = new Form
        {
            Title = "Benchmark",
            Size = new Size(width, height),
            Content = root,
        };

        // registerServices: false — the services are already registered
        // above, and replacing them with stand-ins is not allowed.
        new HeadlessPlatform(registerServices: false).CreateWindow(form);

        return new Scene
        {
            Form = form,
            Surface = CreateSurface(width, height),
            Width = width,
            Height = height,
            ElementCount = Count(root),
        };
    }

    /// <summary>
    /// A text scene: many captions of different lengths. Hits exactly
    /// SkiaFontCache.GetLine and SkiaTextMeasurer.MeasureText — the paths
    /// the text caches were introduced for.
    /// </summary>
    public static Scene TextHeavy(
        int labels = 300,
        int width = DefaultWidth,
        int height = DefaultHeight)
    {
        EnsureServices();

        var panel = new UniformGrid
        {
            Padding = new Thickness(4),
            SpacingX = 4,
            SpacingY = 2,
            OverflowY = Overflow.Auto,
        };

        for (int i = 0; i < labels; i++)
        {
            panel.Children.Add(new Label
            {
                Text = $"Строка номер {i}: краткое описание позиции в списке",
            });
        }

        var form = new Form
        {
            Title = "Benchmark: text",
            Size = new Size(width, height),
            Content = panel,
        };

        new HeadlessPlatform(registerServices: false).CreateWindow(form);

        return new Scene
        {
            Form = form,
            Surface = CreateSurface(width, height),
            Width = width,
            Height = height,
            ElementCount = Count(panel),
        };
    }

    public static SKSurface CreateSurface(int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        return SKSurface.Create(info)
            ?? throw new InvalidOperationException(
                "Could not create an offscreen Skia surface.");
    }

    /// <summary>The number of elements in a subtree — for context in the report.</summary>
    public static int Count(UIElement element)
    {
        int total = 1;

        switch (element)
        {
            case WrapControl wrap when wrap.Child is not null:
                total += Count(wrap.Child);
                break;

            case PanelControl panel:
                foreach (UIElement child in panel.Children)
                    total += Count(child);
                break;
        }

        return total;
    }
}