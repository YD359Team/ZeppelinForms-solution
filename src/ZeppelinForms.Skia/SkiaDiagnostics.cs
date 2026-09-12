namespace ZeppelinForms.Skia;

/// <summary>
/// Счётчики внутренних кэшей. Нужны бенчмаркам: по удержанной памяти
/// нельзя отличить постоянный рабочий объём ограниченного кэша
/// от настоящего роста, а число записей отвечает на это прямо.
/// </summary>
public static class SkiaDiagnostics
{
    /// <summary>Записей в кэше подстановок шрифтов (оба поколения).</summary>
    public static int FallbackEntries => SkiaFontCache.FallbackCount;

    /// <summary>Записей в кэше разобранных строк текущего потока
    /// (оба поколения). Кэш потоковый, поэтому значение относится
    /// к тому потоку, который спрашивает.</summary>
    public static int LineEntries => SkiaFontCache.LineCount;
}