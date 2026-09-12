using System.Text.Json.Serialization;

namespace ZeppelinForms.Benchmarks;

/// <summary>
/// Результат одного сценария. Поля намеренно плоские и скалярные —
/// файл эталона читается глазами в diff'е пулл-реквеста.
/// </summary>
public sealed record BenchmarkResult
{
    /// <summary>Имя сценария. Оно же ключ в эталоне.</summary>
    public required string Name { get; init; }

    /// <summary>Сколько полезных итераций выполнено (без прогрева).</summary>
    public required int Iterations { get; init; }

    /// <summary>Медианное время итерации, мс.</summary>
    public required double MedianMs { get; init; }

    /// <summary>95-й процентиль времени итерации, мс.
    /// Именно он ловит подтормаживания, которые видит пользователь,
    /// а среднее их размазывает.</summary>
    public required double P95Ms { get; init; }

    public required double MaxMs { get; init; }

    /// <summary>Байт аллокаций на одну итерацию. Главная метрика фазы 1:
    /// пул SKPaint и кэш измерений текста бьют именно по ней.</summary>
    public required long AllocatedBytesPerIteration { get; init; }

    /// <summary>Сколько памяти осталось занято после полной сборки мусора.
    /// Растёт только там, где что-то удерживается — статические кэши,
    /// закреплённые буферы, неосвобождённые нативные объекты.</summary>
    public required long RetainedBytes { get; init; }

    /// <summary>Прирост рабочего множества процесса, байт. Единственная
    /// метрика, которая видит нативную сторону Skia: копии пикселей
    /// в SKImage в управляемую кучу не попадают.</summary>
    public required long WorkingSetDeltaBytes { get; init; }

    public required int Gen0Collections { get; init; }
    public required int Gen1Collections { get; init; }
    public required int Gen2Collections { get; init; }

    [JsonIgnore]
    public string ShortSummary =>
        $"{MedianMs,8:F3} ms  p95 {P95Ms,8:F3} ms  " +
        $"{Format.Bytes(AllocatedBytesPerIteration),10}/iter  " +
        $"retained {Format.Bytes(RetainedBytes),10}";
}

/// <summary>Файл эталона целиком.</summary>
public sealed record BaselineFile
{
    /// <summary>Ось, для которой снят эталон. Отрисовка текста
    /// отличается между платформами, поэтому эталоны раздельные —
    /// как и снимки в tests/.../Snapshots/Expected/{win,linux}.</summary>
    public required string Platform { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required Dictionary<string, BenchmarkResult> Results { get; init; }
}

public static class Format
{
    /// <summary>Человекочитаемый размер. Бенчмарк читают глазами
    /// чаще, чем машиной, поэтому байты не показываем.</summary>
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