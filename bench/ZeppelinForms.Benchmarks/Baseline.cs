using System.Text.Json;

namespace ZeppelinForms.Benchmarks;

/// <summary>Какие метрики сценария имеет смысл сравнивать с эталоном.</summary>
[Flags]
public enum GatedMetrics
{
    None = 0,

    /// <summary>Медиана времени итерации.</summary>
    Time = 1,

    /// <summary>Байты аллокаций на итерацию.</summary>
    Allocations = 2,

    /// <summary>Память, оставшаяся занятой после полной сборки мусора.</summary>
    Retained = 4,
}

/// <summary>Одно расхождение с эталоном.</summary>
public sealed record Regression(
    string Scenario,
    string Metric,
    double Baseline,
    double Current,
    double Ratio);

public static class Baseline
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Метрики, проверяемые для конкретных сценариев. Всё, чего здесь нет,
    /// проверяется по <see cref="DefaultMetrics"/>.
    /// </summary>
    private static readonly Dictionary<string, GatedMetrics> ScenarioMetrics = new()
    {
        // Время здесь — это чтение файлов шрифтов: на холодном файловом
        // кэше ОС сценарий медленнее в десятки раз, и гейт ловил бы
        // состояние машины, а не код. Смысл сценария в удержании.
        ["memory.font-fallback-growth"] = GatedMetrics.Retained,

        // Аллокации на итерацию здесь — это буфер, который создаёт сам
        // бенчмарк; проверяем его как признак того, что сценарий не
        // изменился, и удержание как собственно предмет измерения.
        ["memory.image-retention"] = GatedMetrics.Allocations | GatedMetrics.Retained,

        // Каждая итерация промахивается мимо кэшей по построению, так что
        // время — это стоимость промаха, а не работа приложения. Предмет
        // измерения — удержание: потолки не дают ему расти.
        ["memory.text-churn"] = GatedMetrics.Retained,
    };

    /// <summary>
    /// По умолчанию удержание не проверяется: в сценариях раскладки
    /// и отрисовки оно колеблется около нуля в обе стороны и дало бы
    /// срабатывания на шуме.
    /// </summary>
    private const GatedMetrics DefaultMetrics = GatedMetrics.Time | GatedMetrics.Allocations;

    /// <summary>Ось, для которой снят эталон. Отрисовка текста
    /// отличается между платформами, поэтому эталоны раздельные —
    /// как и снимки в tests/.../Snapshots/Expected/{win,linux}.</summary>
    public static string CurrentPlatform =>
        OperatingSystem.IsWindows() ? "win"
        : OperatingSystem.IsLinux() ? "linux"
        : OperatingSystem.IsMacOS() ? "macos"
        : "unknown";

    public static string DefaultPath(string directory) =>
        Path.Combine(directory, $"baseline-{CurrentPlatform}.json");

    public static void Save(string path, IEnumerable<BenchmarkResult> results)
    {
        var file = new BaselineFile
        {
            Platform = CurrentPlatform,
            CreatedUtc = DateTimeOffset.UtcNow,
            Results = results.ToDictionary(r => r.Name),
        };

        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(file, Json));
    }

    public static BaselineFile? Load(string path)
    {
        if (!File.Exists(path)) return null;

        return JsonSerializer.Deserialize<BaselineFile>(File.ReadAllText(path));
    }

    /// <summary>
    /// Сравнение с эталоном. Пороги разные по смыслу: время замеряется
    /// на общем раннере и шумит, аллокации детерминированы и шуметь
    /// не должны вовсе.
    /// </summary>
    /// <param name="timeTolerance">Допустимый рост медианы, доля.</param>
    /// <param name="allocationTolerance">Допустимый рост аллокаций, доля.</param>
    /// <summary>
    /// Сравнение с эталоном. Пороги разные по смыслу: время замеряется
    /// на общем раннере и шумит, аллокации детерминированы и шуметь
    /// не должны вовсе.
    /// </summary>
    /// <param name="timeTolerance">Допустимый рост медианы, доля.</param>
    /// <param name="allocationTolerance">Допустимый рост аллокаций, доля.</param>
    public static List<Regression> Compare(
        BaselineFile baseline,
        IEnumerable<BenchmarkResult> current,
        double timeTolerance = 0.20,
        double allocationTolerance = 0.05)
    {
        // Абсолютные полы. После фазы 1 аллокации измеряются десятками байт,
        // и один относительный порог превратил бы гейт в генератор ложных
        // срабатываний: при эталоне в 72 байта рост на четыре байта — это
        // уже +5%. Расхождение засчитывается, только когда оно заметно
        // и в долях, и в абсолютных величинах.
        const double timeFloorMs = 0.05;
        const double byteFloor = 1024;

        var regressions = new List<Regression>();

        foreach (BenchmarkResult result in current)
        {
            if (!baseline.Results.TryGetValue(result.Name, out BenchmarkResult? old))
                continue;   // новый сценарий — сравнивать не с чем

            GatedMetrics metrics = ScenarioMetrics.TryGetValue(result.Name, out GatedMetrics custom)
                ? custom
                : DefaultMetrics;

            if (metrics.HasFlag(GatedMetrics.Time))
                Check(result.Name, "median-ms",
                    old.MedianMs, result.MedianMs, timeTolerance, timeFloorMs);

            if (metrics.HasFlag(GatedMetrics.Allocations))
                Check(result.Name, "alloc-per-iter",
                    old.AllocatedBytesPerIteration, result.AllocatedBytesPerIteration,
                    allocationTolerance, byteFloor);

            if (metrics.HasFlag(GatedMetrics.Retained))
                Check(result.Name, "retained-bytes",
                    old.RetainedBytes, result.RetainedBytes,
                    allocationTolerance, byteFloor);
        }

        return regressions;

        void Check(string scenario, string metric, double before, double after, double tolerance, double floor)
        {
            double delta = after - before;

            // шум в пределах пола не разбираем независимо от процентов
            if (delta <= floor) return;

            // нулевой эталон делить нельзя, а рост с нуля до заметной
            // величины всё равно надо показать
            if (before <= 0)
            {
                regressions.Add(new Regression(scenario, metric, before, after, double.PositiveInfinity));
                return;
            }

            double ratio = delta / before;

            if (ratio > tolerance)
                regressions.Add(new Regression(scenario, metric, before, after, ratio));
        }
    }
}