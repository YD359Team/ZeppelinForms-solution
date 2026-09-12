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
/// Сцена: форма с деревом контролов плюс offscreen-поверхность Skia,
/// в которую её рисуют. Держит всё, что нужно одному сценарию,
/// и освобождает нативную поверхность в Dispose.
/// </summary>
public sealed class Scene : IDisposable
{
    public required Form Form { get; init; }
    public required SKSurface Surface { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    public SKCanvas Canvas => Surface.Canvas;

    /// <summary>Сколько элементов в дереве — печатается рядом
    /// с результатом, иначе цифры не с чем соотнести.</summary>
    public required int ElementCount { get; init; }

    public void Dispose() => Surface.Dispose();
}

public static class Scenes
{
    public const int DefaultWidth = 1280;
    public const int DefaultHeight = 800;

    private static bool _servicesRegistered;

    /// <summary>
    /// Headless-платформа с настоящими Skia-сервисами. Ровно тот же
    /// набор, что в SnapshotFixture: заглушки из Headless мерить
    /// бессмысленно — HeadlessGraphics не делает ничего.
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
    /// Типовая деловая форма: заголовок, панель кнопок, сетка полей,
    /// список. Нарочно без анимаций и эффектов — меряем базовую
    /// стоимость обычного окна, а не пиковую.
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

        // registerServices: false — сервисы уже зарегистрированы
        // выше и заменять их заглушками нельзя.
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
    /// Текстовая сцена: много подписей разной длины. Бьёт ровно
    /// по SkiaFontCache.SplitRuns и SkiaTextMeasurer.MeasureText —
    /// тем местам, где сейчас нет кэша и строка перебирается по рунам.
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
                "Не удалось создать offscreen-поверхность Skia.");
    }

    /// <summary>Число элементов в поддереве — для контекста в отчёте.</summary>
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