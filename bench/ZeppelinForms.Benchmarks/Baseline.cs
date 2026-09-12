using System.Text.Json;

namespace ZeppelinForms.Benchmarks;

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

    /// <summary>Отрисовка текста различается между платформами,
    /// поэтому эталоны раздельные — как и снимки.</summary>
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
    public static List<Regression> Compare(
        BaselineFile baseline,
        IEnumerable<BenchmarkResult> current,
        double timeTolerance = 0.20,
        double allocationTolerance = 0.05)
    {
        var regressions = new List<Regression>();

        foreach (BenchmarkResult result in current)
        {
            if (!baseline.Results.TryGetValue(result.Name, out BenchmarkResult? old))
                continue;   // новый сценарий — сравнивать не с чем

            Check(result.Name, "median-ms",
                old.MedianMs, result.MedianMs, timeTolerance);

            Check(result.Name, "alloc-per-iter",
                old.AllocatedBytesPerIteration, result.AllocatedBytesPerIteration,
                allocationTolerance);

            Check(result.Name, "retained-bytes",
                old.RetainedBytes, result.RetainedBytes, allocationTolerance);
        }

        return regressions;

        void Check(string scenario, string metric, double before, double after, double tolerance)
        {
            // нулевой эталон делить нельзя, а рост с нуля до заметной
            // величины всё равно надо показать
            if (before <= 0)
            {
                if (after > 4096)
                    regressions.Add(new Regression(scenario, metric, before, after, double.PositiveInfinity));

                return;
            }

            double ratio = (after - before) / before;

            if (ratio > tolerance)
                regressions.Add(new Regression(scenario, metric, before, after, ratio));
        }
    }
}