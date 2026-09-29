using System.Diagnostics;

namespace ZeppelinForms.Benchmarks;

/// <summary>
/// A scenario description. setup runs once and its cost doesn't count
/// towards the measurement; body is what we measure.
/// </summary>
public sealed class Benchmark
{
    public required string Name { get; init; }

    /// <summary>A short explanation of what exactly is loaded. Printed next to
    /// the result, so that there is no guessing half a year later.</summary>
    public required string Description { get; init; }

    /// <summary>Scene preparation. Returns the state body gets: this way the scene
    /// is built once rather than on every iteration.</summary>
    public required Func<object> Setup { get; init; }

    /// <summary>One iteration. The argument is what Setup returned.</summary>
    public required Action<object> Body { get; init; }

    /// <summary>How many useful iterations. Expensive scenarios have fewer.</summary>
    public int Iterations { get; init; } = 200;

    /// <summary>Warm-up iterations: JIT, lazy cache initialization, the first font
    /// load. Without them the first iteration costs several times more than the rest
    /// and spoils both the median and the maximum.</summary>
    public int WarmupIterations { get; init; } = 20;

    /// <summary>
    /// What to show next to the result besides the runner's numbers. Called once
    /// after the measurement; the argument is the state from Setup.
    /// </summary>
    /// <remarks>Needed where aggregate metrics don't answer the scenario's question:
    /// retained memory can't tell the working set of a bounded cache from growth,
    /// while the number of entries can.</remarks>
    public Func<object, string>? Report { get; init; }
}

public static class BenchmarkRunner
{
    public static BenchmarkResult Run(Benchmark benchmark, int? iterationsOverride = null)
    {
        int iterations = iterationsOverride ?? benchmark.Iterations;

        object state = benchmark.Setup();

        for (int i = 0; i < benchmark.WarmupIterations; i++)
            benchmark.Body(state);

        // The reference point is taken after the warm-up: whatever the scenario
        // allocated while warming up has nothing to do with regressions.
        Settle();

        long workingSetBefore = CurrentWorkingSet();
        long heapBefore = GC.GetTotalMemory(forceFullCollection: false);
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);

        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);

        var timings = new double[iterations];

        for (int i = 0; i < iterations; i++)
        {
            long start = Stopwatch.GetTimestamp();
            benchmark.Body(state);
            timings[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        long allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);

        int gen0After = GC.CollectionCount(0);
        int gen1After = GC.CollectionCount(1);
        int gen2After = GC.CollectionCount(2);

        // Retained memory is measured only after a full collection: otherwise the
        // number would include garbage that simply hasn't been collected yet, and
        // a scenario without leaks would look like one with a leak.
        Settle();

        long heapAfter = GC.GetTotalMemory(forceFullCollection: false);
        long workingSetAfter = CurrentWorkingSet();

        // After the allocation measurement: the report itself builds a string,
        // and placed above it would count towards the scenario's allocations.
        string? report = benchmark.Report?.Invoke(state);

        Array.Sort(timings);

        return new BenchmarkResult
        {
            Name = benchmark.Name,
            Iterations = iterations,
            MedianMs = Percentile(timings, 0.50),
            P95Ms = Percentile(timings, 0.95),
            MaxMs = timings[^1],
            AllocatedBytesPerIteration = (allocatedAfter - allocatedBefore) / iterations,
            RetainedBytes = heapAfter - heapBefore,
            WorkingSetDeltaBytes = workingSetAfter - workingSetBefore,
            Gen0Collections = gen0After - gen0Before,
            Gen1Collections = gen1After - gen1Before,
            Gen2Collections = gen2After - gen2Before,
            Report = report,
        };
    }

    /// <summary>
    /// Bring the heap to rest. Two passes are mandatory: SkiaSharp wrappers have
    /// finalizers, and an object freed in the first pass goes into the finalization
    /// queue, while its memory comes back only in the second.
    /// </summary>
    private static void Settle()
    {
        for (int i = 0; i < 2; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
        }

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
    }

    private static long CurrentWorkingSet()
    {
        // Refresh is mandatory: Process caches a snapshot of the counters
        // from the moment the object was created.
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.WorkingSet64;
    }

    /// <summary>A percentile over an already sorted array.</summary>
    private static double Percentile(double[] sorted, double q)
    {
        if (sorted.Length == 0) return 0;
        if (sorted.Length == 1) return sorted[0];

        double position = q * (sorted.Length - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);

        if (lower == upper) return sorted[lower];

        double weight = position - lower;
        return sorted[lower] * (1 - weight) + sorted[upper] * weight;
    }
}