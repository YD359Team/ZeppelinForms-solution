using System.Text.Json.Serialization;

namespace ZeppelinForms.Benchmarks;

/// <summary>
/// The result of one scenario. The fields are deliberately flat and scalar —
/// the baseline file is read by eye in a pull request diff.
/// </summary>
public sealed record BenchmarkResult
{
    /// <summary>The scenario name. Also the key in the baseline.</summary>
    public required string Name { get; init; }

    /// <summary>How many useful iterations were run (without the warm-up).</summary>
    public required int Iterations { get; init; }

    /// <summary>The median iteration time, ms.</summary>
    public required double MedianMs { get; init; }

    /// <summary>The 95th percentile of iteration time, ms.
    /// It is what catches the stutters the user sees,
    /// while the average smears them out.</summary>
    public required double P95Ms { get; init; }

    public required double MaxMs { get; init; }

    /// <summary>Bytes allocated per iteration. The main metric of phase 1:
    /// the SKPaint pool and the text measurement cache hit exactly this one.</summary>
    public required long AllocatedBytesPerIteration { get; init; }

    /// <summary>How much memory stayed occupied after a full garbage collection.
    /// Grows only where something is retained — static caches, pinned buffers,
    /// unreleased native objects.</summary>
    public required long RetainedBytes { get; init; }

    /// <summary>The growth of the process working set, bytes. The only metric that
    /// sees Skia's native side: pixel copies in SKImage don't get into the managed heap.</summary>
    public required long WorkingSetDeltaBytes { get; init; }

    public required int Gen0Collections { get; init; }
    public required int Gen1Collections { get; init; }
    public required int Gen2Collections { get; init; }

    /// <summary>The scenario's own report, if it gives one.</summary>
    /// <remarks>Not written into the baseline: this is diagnostics for the eye,
    /// not a metric for comparison. A gate must not be built on it —
    /// the counters depend on the number of iterations.</remarks>
    [JsonIgnore]
    public string? Report { get; init; }

    [JsonIgnore]
    public string ShortSummary =>
        $"{MedianMs,8:F3} ms  p95 {P95Ms,8:F3} ms  " +
        $"{Format.Bytes(AllocatedBytesPerIteration),10}/iter  " +
        $"retained {Format.Bytes(RetainedBytes),10}" +
        (Report is null ? "" : $"  {Report}");
}

/// <summary>The whole baseline file.</summary>
public sealed record BaselineFile
{
    /// <summary>The platform the baseline was taken for. Text rendering differs
    /// between platforms, so the baselines are separate — like the snapshots
    /// in tests/.../Snapshots/Expected/{win,linux}.</summary>
    public required string Platform { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required Dictionary<string, BenchmarkResult> Results { get; init; }
}

public static class Format
{
    /// <summary>A human-readable size. A benchmark is read by eye more often
    /// than by a machine, so raw bytes are not shown.</summary>
    public static string Bytes(long value)
    {
        double abs = Math.Abs((double)value);
        string sign = value < 0 ? "-" : "";

        return abs switch
        {
            >= 1024L * 1024 * 1024 => $"{sign}{abs / (1024d * 1024 * 1024):F2} GB",
            >= 1024 * 1024 => $"{sign}{abs / (1024d * 1024):F2} MB",
            >= 1024 => $"{sign}{abs / 1024d:F1} KB",
            _ => $"{value} B",
        };
    }

    public static string Percent(double ratio) =>
        (ratio >= 0 ? "+" : "") + (ratio * 100).ToString("F1") + "%";
}