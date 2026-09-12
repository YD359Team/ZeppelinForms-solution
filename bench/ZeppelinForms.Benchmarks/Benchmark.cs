using System.Diagnostics;

namespace ZeppelinForms.Benchmarks;

/// <summary>
/// Описание сценария. setup выполняется один раз и его стоимость
/// в измерение не входит; body — то, что меряем.
/// </summary>
public sealed class Benchmark
{
    public required string Name { get; init; }

    /// <summary>Короткое пояснение, что именно нагружается. Печатается
    /// рядом с результатом, чтобы через полгода не гадать.</summary>
    public required string Description { get; init; }

    /// <summary>Подготовка сцены. Возвращает состояние, которое
    /// получает body: так сцена строится один раз, а не на каждой итерации.</summary>
    public required Func<object> Setup { get; init; }

    /// <summary>Одна итерация. Аргумент — то, что вернул Setup.</summary>
    public required Action<object> Body { get; init; }

    /// <summary>Сколько полезных итераций. У дорогих сценариев меньше.</summary>
    public int Iterations { get; init; } = 200;

    /// <summary>Итерации прогрева: JIT, ленивая инициализация кэшей,
    /// первая загрузка шрифта. Без них первая итерация в разы дороже
    /// остальных и портит и медиану, и максимум.</summary>
    public int WarmupIterations { get; init; } = 20;
}

public static class BenchmarkRunner
{
    public static BenchmarkResult Run(Benchmark benchmark, int? iterationsOverride = null)
    {
        int iterations = iterationsOverride ?? benchmark.Iterations;

        object state = benchmark.Setup();

        for (int i = 0; i < benchmark.WarmupIterations; i++)
            benchmark.Body(state);

        // Точка отсчёта берётся после прогрева: всё, что сценарий
        // выделил на разогреве, к регрессиям отношения не имеет.
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

        // Удержанное меряем только после полной уборки: иначе в цифру
        // попадёт мусор, который просто не успели собрать, и сценарий
        // без утечек выглядел бы как сценарий с утечкой.
        Settle();

        long heapAfter = GC.GetTotalMemory(forceFullCollection: false);
        long workingSetAfter = CurrentWorkingSet();

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
        };
    }

    /// <summary>
    /// Довести кучу до покоя. Два прохода обязательны: у обёрток
    /// SkiaSharp есть финализаторы, и объект, освобождённый в первом
    /// проходе, попадает в очередь финализации, а его память
    /// возвращается только во втором.
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
        // Refresh обязателен: Process кэширует снимок счётчиков
        // с момента создания объекта.
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.WorkingSet64;
    }

    /// <summary>Процентиль по уже отсортированному массиву.</summary>
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