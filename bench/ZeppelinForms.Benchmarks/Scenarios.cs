using SkiaSharp;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Skia;

namespace ZeppelinForms.Benchmarks;

public static class Scenarios
{
    public static IReadOnlyList<Benchmark> All() =>
    [
        Layout(),
        RenderBusinessForm(),
        RenderTextHeavy(),
        MeasureText(),
        ImageRetention(),
        FontFallbackRetention(),
        TextChurn(),
    ];

    /// <summary>
    /// Полный проход раскладки. Measure и Arrange публичные, а вот
    /// Form.PerformLayout — internal, поэтому дёргаем Content напрямую.
    /// Кэша валидности раскладки нет, так что каждый вызов честный.
    /// </summary>
    private static Benchmark Layout() => new()
    {
        Name = "layout.business-form",
        Description = "Measure + Arrange всего дерева деловой формы",
        Iterations = 300,
        Setup = () => Scenes.BusinessForm(),
        Body = state =>
        {
            var scene = (Scene)state;
            var size = new Size(scene.Width, scene.Height);

            scene.Form.Content!.Measure(size);
            scene.Form.Content!.Arrange(new Rectangle(Point.Empty, size));
        },
    };

    /// <summary>
    /// Кадр целиком. Главный сценарий фазы 1: сюда попадают
    /// и 25 мест `new SKPaint` в SkiaGraphics, и тройной вызов
    /// SplitRuns в DrawRuns.
    /// </summary>
    private static Benchmark RenderBusinessForm() => new()
    {
        Name = "render.business-form",
        Description = "SkiaRenderer.Render всей формы в offscreen-поверхность",
        Iterations = 200,
        Setup = () => Scenes.BusinessForm(),
        Body = state =>
        {
            var scene = (Scene)state;
            SkiaRenderer.Render(scene.Form, scene.Canvas);
            scene.Canvas.Flush();
        },
        Report = _ =>
            $"paints {SkiaDiagnostics.PaintsCreated}, " +
            $"lines {SkiaDiagnostics.LineEntries}",
    };

    /// <summary>Тот же кадр, но с преобладанием текста.</summary>
    private static Benchmark RenderTextHeavy() => new()
    {
        Name = "render.text-heavy",
        Description = "Кадр из 300 подписей — нагрузка на отрисовку текста",
        Iterations = 150,
        Setup = () => Scenes.TextHeavy(),
        Body = state =>
        {
            var scene = (Scene)state;
            SkiaRenderer.Render(scene.Form, scene.Canvas);
            scene.Canvas.Flush();
        },
    };

    /// <summary>
    /// Чистое измерение текста, без отрисовки. Сейчас кэша нет вовсе:
    /// SkiaTextMeasurer.MeasureText каждый раз заново перебирает руны
    /// и вызывает ContainsGlyph на каждый символ. После фазы 1
    /// эта цифра должна упасть на порядок.
    /// </summary>
    private static Benchmark MeasureText() => new()
    {
        Name = "text.measure-repeated",
        Description = "1000 повторных измерений одних и тех же строк",
        Iterations = 100,
        Setup = () =>
        {
            Scenes.EnsureServices();

            string[] samples = new string[50];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = $"Позиция {i}: наименование товара и единица измерения";

            return new MeasureState(new SkiaTextMeasurer(), samples, Font.Default);
        },
        Body = state =>
        {
            var measure = (MeasureState)state;

            for (int repeat = 0; repeat < 20; repeat++)
                foreach (string sample in measure.Samples)
                    measure.Measurer.MeasureText(sample, measure.Font);
        },
    };

    private sealed record MeasureState(
        SkiaTextMeasurer Measurer,
        string[] Samples,
        Font Font);

    /// <summary>
    /// Детектор удержания картинок. Каждая итерация создаёт новую
    /// Image и рисует её. SkiaGraphics.GetOrCreate закрепляет
    /// image.Pixels через GCHandle и делает нативную копию в SKImage;
    /// ConditionalWeakTable должен отпустить запись, когда Image
    /// станет недостижимой. Если RetainedBytes или WorkingSetDelta
    /// растут линейно — не отпускает.
    /// </summary>
    private static Benchmark ImageRetention() => new()
    {
        Name = "memory.image-retention",
        Description = "Создание и отрисовка одноразовых изображений 256x256",
        Iterations = 200,
        WarmupIterations = 10,
        Setup = () =>
        {
            Scenes.EnsureServices();

            SKSurface surface = Scenes.CreateSurface(512, 512);
            return new ImageState(surface, new SkiaGraphics(surface.Canvas));
        },
        Body = state =>
        {
            var images = (ImageState)state;

            const int side = 256;
            var image = new Image(side, side, new byte[side * side * 4]);

            images.Graphics.DrawImage(new Rectangle(0, 0, side, side), image);
            images.Surface.Canvas.Flush();
        },
        Report = _ => $"uploads {SkiaDiagnostics.ImagesUploaded}",
    };

    private sealed record ImageState(SKSurface Surface, SkiaGraphics Graphics);

    /// <summary>
    /// Рост кэша подстановок шрифтов. Ключ Fallbacks — это
    /// (family, weight, style, codepoint), то есть запись на каждый
    /// уникальный символ вне основного шрифта. Сценарий кормит
    /// измеритель редкими кодовыми точками и смотрит, сколько
    /// памяти остаётся занято после сборки мусора.
    /// </summary>
    /// <remarks>
    /// Задевает два кэша сразу: 32 новых кодпоинта и одну новую строку
    /// на итерацию, то есть записи копятся и в Fallbacks, и в Lines.
    /// Поэтому в отчёт идут оба счётчика: по одной удержанной памяти
    /// их вклады не разделить.
    /// </remarks>
    private static Benchmark FontFallbackRetention() => new()
    {
        Name = "memory.font-fallback-growth",
        Description = "Измерение текста с редкими кодовыми точками (рост Fallbacks)",
        Iterations = 100,
        WarmupIterations = 5,
        Setup = () =>
        {
            Scenes.EnsureServices();
            return new FallbackState(new SkiaTextMeasurer(), 0);
        },
        Body = state =>
        {
            var fallback = (FallbackState)state;

            // диапазон CJK: символы почти наверняка отсутствуют
            // в базовом латинском шрифте и уходят в подстановку
            int start = 0x4E00 + fallback.Next * 32;
            fallback.Next++;

            var builder = new System.Text.StringBuilder(32);
            for (int i = 0; i < 32; i++)
                builder.Append(char.ConvertFromUtf32(start + i));

            fallback.Measurer.MeasureText(builder.ToString(), Font.Default);
        },

        // Ограниченный кэш держит число записей около лимита независимо
        // от того, сколько символов через него прошло. Растущее число —
        // признак того, что потолок не работает.
        Report = _ =>
            $"fallbacks {SkiaDiagnostics.FallbackEntries}, lines {SkiaDiagnostics.LineEntries}",
    };

    /// <summary>
    /// Текст и кегль, меняющиеся на каждой итерации. Проверяет потолки
    /// кэшей: и строка, и размер шрифта каждый раз новые, поэтому
    /// без ограничения Lines и Fonts росли бы линейно — так ведут себя
    /// часы, счётчики и живая фильтрация списка.
    /// </summary>
    /// <remarks>
    /// Время здесь второстепенно: каждая итерация промахивается мимо
    /// обоих кэшей по построению, и меряется стоимость промаха, а не
    /// работа приложения. Предмет измерения — счётчики и удержание.
    /// </remarks>
    private static Benchmark TextChurn() => new()
    {
        Name = "memory.text-churn",
        Description = "Уникальная строка и кегль на каждой итерации (потолки Lines и Fonts)",
        Iterations = 600,
        WarmupIterations = 10,
        Setup = () =>
        {
            Scenes.EnsureServices();
            return new ChurnState(new SkiaTextMeasurer(), 0);
        },
        Body = state =>
        {
            var churn = (ChurnState)state;
            int n = churn.Next++;

            // кегль дробный и всякий раз новый: Font — запись, Size входит
            // в её равенство, поэтому каждое значение заводит свой SKFont
            Font font = Font.Default.WithSize(14f + n * 0.01f);

            churn.Measurer.MeasureText($"Обновление {n}: значение счётчика", font);
        },

        Report = _ =>
            $"lines {SkiaDiagnostics.LineEntries}, " +
            $"fonts {SkiaDiagnostics.FontEntries}, " +
            $"sized {SkiaDiagnostics.SizedFontEntries}",
    };

    private sealed class ChurnState(SkiaTextMeasurer measurer, int next)
    {
        public SkiaTextMeasurer Measurer { get; } = measurer;
        public int Next { get; set; } = next;
    }

    private sealed class FallbackState(SkiaTextMeasurer measurer, int next)
    {
        public SkiaTextMeasurer Measurer { get; } = measurer;
        public int Next { get; set; } = next;
    }
}