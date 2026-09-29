using System.Text.Json;

namespace ZeppelinForms.Benchmarks;

/// <summary>Which scenario metrics make sense to compare with the baseline.</summary>
[Flags]
public enum GatedMetrics
{
    None = 0,

    /// <summary>The median iteration time.</summary>
    Time = 1,

    /// <summary>Bytes allocated per iteration.</summary>
    Allocations = 2,

    /// <summary>Memory that stayed occupied after a full garbage collection.</summary>
    Retained = 4,
}

/// <summary>One divergence from the baseline.</summary>
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
    /// Metrics checked for specific scenarios. Everything not listed here
    /// is checked by <see cref="DefaultMetrics"/>.
    /// </summary>
    private static readonly Dictionary<string, GatedMetrics> ScenarioMetrics = new()
    {
        // The time here is reading font files: with a cold OS file cache the scenario
        // is dozens of times slower, and the gate would catch the machine's state
        // rather than the code. The point of the scenario is retention.
        ["memory.font-fallback-growth"] = GatedMetrics.Retained,

        // The allocations per iteration here are the buffer the benchmark itself
        // creates; they are checked as a sign that the scenario hasn't changed, and
        // retention as the actual subject of the measurement.
        ["memory.image-retention"] = GatedMetrics.Allocations | GatedMetrics.Retained,

        // No aggregate metric fits a gate here: the time measures the cost of
        // missing the caches, and retention is proportional to the number of
        // iterations, because each one adds a new entry. The point of the scenario
        // is in the counters from Report: they show whether the ceilings hold.
        ["memory.text-churn"] = GatedMetrics.None,
    };

    /// <summary>
    /// By default retention is not checked: in layout and drawing scenarios it
    /// fluctuates around zero in both directions and would fire on noise.
    /// </summary>
    private const GatedMetrics DefaultMetrics = GatedMetrics.Time | GatedMetrics.Allocations;

    /// <summary>The platform the baseline is taken for. Text rendering differs
    /// between platforms, so the baselines are separate — like the snapshots
    /// in tests/.../Snapshots/Expected/{win,linux}.</summary>
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
    /// Comparison with the baseline. The thresholds differ in meaning: time is
    /// measured on a shared runner and is noisy, while allocations are
    /// deterministic and must not be noisy at all.
    /// </summary>
    /// <param name="timeTolerance">The allowed growth of the median, as a fraction.</param>
    /// <param name="allocationTolerance">The allowed growth of allocations, as a fraction.</param>
    public static List<Regression> Compare(
        BaselineFile baseline,
        IEnumerable<BenchmarkResult> current,
        double timeTolerance = 0.20,
        double allocationTolerance = 0.05)
    {
        // Absolute floors. After phase 1 allocations are measured in tens of bytes,
        // and a relative threshold alone would turn the gate into a generator of
        // false alarms: with a baseline of 72 bytes, growth by four bytes is already
        // +5%. A divergence counts only when it is noticeable both as a fraction
        // and in absolute terms.
        const double timeFloorMs = 0.05;
        const double byteFloor = 1024;

        var regressions = new List<Regression>();

        foreach (BenchmarkResult result in current)
        {
            if (!baseline.Results.TryGetValue(result.Name, out BenchmarkResult? old))
                continue;   // a new scenario — there is nothing to compare with

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

            // noise within the floor is not examined, regardless of percentages
            if (delta <= floor) return;

            // a zero baseline can't be divided by, but growth from zero
            // to a noticeable value must still be shown
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