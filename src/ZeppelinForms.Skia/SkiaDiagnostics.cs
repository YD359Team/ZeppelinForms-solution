namespace ZeppelinForms.Skia;

/// <summary>
/// Счётчики внутренних кэшей и пулов. Нужны бенчмаркам и тестам:
/// по удержанной памяти нельзя отличить постоянный рабочий объём
/// ограниченного кэша от настоящего роста, а число записей отвечает
/// на это прямо.
/// </summary>
/// <remarks>
/// Часть счётчиков потоковые — ровно там, где потоковый сам кэш.
/// Значение относится к тому потоку, который спрашивает, и сравнивать
/// его со значением из другого потока бессмысленно.
/// </remarks>
public static class SkiaDiagnostics
{
    /// <summary>Записей в кэше подстановок шрифтов (оба поколения).
    /// Ограничен: ключ включает кодпоинт, и без потолка словарь рос бы
    /// на каждый встреченный символ.</summary>
    public static int FallbackEntries => SkiaFontCache.FallbackCount;

    /// <summary>Записей в кэше разобранных строк текущего потока.</summary>
    public static int LineEntries => SkiaFontCache.LineCount;

    /// <summary>Записей в кэше готовых SKFont текущего потока.
    /// Ограничен: Size входит в равенство Font, поэтому анимация кегля
    /// заводит шрифт на каждое промежуточное значение.</summary>
    public static int FontEntries => SkiaFontCache.FontCount;

    /// <summary>Записей в кэше шрифтов подстановок с привязкой к кеглю.</summary>
    public static int SizedFontEntries => SkiaFontCache.SizedFontCount;

    /// <summary>Разрешённых typeface'ов. Без потолка: их единицы
    /// на приложение.</summary>
    public static int TypefaceEntries => SkiaFontCache.TypefaceCount;

    /// <summary>Сколько SKPaint создано в этом потоке. Пул держит две
    /// кисти — на заливку и на обводку, — поэтому рабочее значение
    /// это 0, 1 или 2. Больше означает, что пул не работает.</summary>
    public static int PaintsCreated => SkiaGraphics.PaintsCreated;

    /// <summary>Сколько изображений залито в Skia за время жизни процесса.
    /// Не размер кэша: ConditionalWeakTable не даёт Count. Полезно
    /// в сравнении с числом созданных Image — если растёт быстрее,
    /// кэш не попадает.</summary>
    public static int ImagesUploaded => SkiaGraphics.ImagesUploaded;
}