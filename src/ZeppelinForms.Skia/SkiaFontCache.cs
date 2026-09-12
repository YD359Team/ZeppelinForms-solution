using SkiaSharp;
using System.Text;
using ZeppelinForms.Drawing;

internal static class SkiaFontCache
{
    // Общие на процесс: SKTypeface в Skia потокобезопасен, держать его
    // по экземпляру на поток незачем.
    private static readonly Dictionary<string, SKTypeface> FileTypefaces = [];
    private static readonly Dictionary<(string, FontWeight, FontStyle), SKTypeface> Typefaces = [];
    private static readonly Lock Sync = new();

    /// <summary>
    /// Подстановки по кодпоинту. В отличие от остальных словарей здесь
    /// нужен потолок: ключ включает сам символ, поэтому проход по эмодзи
    /// или по иероглифике заводит запись на каждый встреченный символ,
    /// и словарь рос без границы — это и ловит сценарий
    /// memory.font-fallback-growth.
    ///
    /// Вытеснение без уничтожения: MatchCharacter отдаёт один и тот же
    /// шрифт для целых диапазонов, и уничтожение по одному ключу
    /// испортило бы остальные. Вытесненный объект освободит финализатор,
    /// когда на него не останется ссылок.
    /// </summary>
    private static readonly GenerationalCache<(string, FontWeight, FontStyle, int), FallbackResult> Fallbacks =
        new(1024, disposeEvicted: false);

    internal static int FallbackCount => Fallbacks.Count;

    /// <summary>Разрешённых typeface'ов, из файлов и из системы.
    /// Потолка здесь нет намеренно: их единицы на приложение, ключ —
    /// семейство с начертанием, а не символ.</summary>
    internal static int TypefaceCount
    {
        get { lock (Sync) return FileTypefaces.Count + Typefaces.Count; }
    }

    // SKFont, в отличие от SKTypeface, потокобезопасным не является:
    // MeasureText и ContainsGlyphs меняют его внутреннее состояние.
    // Поэтому всё, что содержит SKFont, живёт по экземпляру на поток —
    // по той же причине, по которой в SkiaGraphics [ThreadStatic] сделан
    // пул кистей: снимковые тесты xunit идут параллельно.
    //
    // Общая блокировка вместо этого стоила бы захвата на каждый вызов
    // MeasurePrefix, а он зовётся на каждое положение каретки.

    [ThreadStatic] private static GenerationalCache<Font, SKFont>? _fonts;
    [ThreadStatic] private static GenerationalCache<(SKTypeface, float), SKFont>? _sizedFonts;
    [ThreadStatic] private static GenerationalCache<(string Text, Font Font), CachedLine>? _lines;

    /// <summary>Версия подбора шрифтов, увиденная этим потоком.</summary>
    [ThreadStatic] private static int _localVersion;

    /// <summary>Общая версия подбора шрифтов. Растёт на каждый сброс;
    /// потоковые кэши подтягиваются к ней лениво, при первом обращении.</summary>
    private static int _version;

    /// <summary>
    /// Готовые SKFont. Потолок нужен из-за размера в ключе: Font —
    /// запись, и Size входит в её равенство, поэтому анимация кегля
    /// или зум интерфейса заводят отдельный шрифт на каждое
    /// промежуточное значение.
    ///
    /// Вытеснение без уничтожения: на SKFont ссылаются FontRun внутри
    /// разобранных строк, а те живут в своём кэше своей жизнью —
    /// уничтоженный при вытеснении шрифт всплыл бы при отрисовке
    /// такой строки.
    /// </summary>
    private static GenerationalCache<Font, SKFont> Fonts
    {
        get
        {
            SyncVersion();
            return _fonts ??= new GenerationalCache<Font, SKFont>(256, disposeEvicted: false);
        }
    }

    /// <summary>Шрифты подстановок, привязанные к кеглю. Потолок и правило
    /// уничтожения — те же, что у <see cref="Fonts"/>, и по той же причине.</summary>
    private static GenerationalCache<(SKTypeface, float), SKFont> SizedFonts
    {
        get
        {
            SyncVersion();
            return _sizedFonts ??= new GenerationalCache<(SKTypeface, float), SKFont>(256, disposeEvicted: false);
        }
    }

    /// <summary>
    /// Разобранные строки. Лимит поколения подобран под интерфейс:
    /// одновременно на экране редко бывает больше нескольких сотен
    /// различных строк, а всё сверх того — ввод в поле, который
    /// устаревает сам.
    /// </summary>
    private static GenerationalCache<(string Text, Font Font), CachedLine> Lines
    {
        get
        {
            SyncVersion();

            // блобы — нативные объекты, вытеснение обязано их освобождать
            return _lines ??= new GenerationalCache<(string Text, Font Font), CachedLine>(
                4096, disposeEvicted: true);
        }
    }

    internal static int LineCount => _lines?.Count ?? 0;
    internal static int FontCount => _fonts?.Count ?? 0;
    internal static int SizedFontCount => _sizedFonts?.Count ?? 0;

    /// <summary>Догнать общую версию: если подбор шрифтов менялся,
    /// потоковые кэши держат устаревшие SKFont и разобранные по ним
    /// строки.</summary>
    private static void SyncVersion()
    {
        int current = Volatile.Read(ref _version);
        if (_localVersion == current) return;

        DropLocal();
        _localVersion = current;
    }

    /// <summary>Освободить потоковые кэши. Порядок важен: сначала строки
    /// вместе с блобами, потом сами шрифты — строки ссылаются на шрифты
    /// через FontRun, и обратный порядок оставил бы висячие ссылки.</summary>
    private static void DropLocal()
    {
        _lines?.Clear();

        // ссылок на шрифты после очистки строк не осталось,
        // поэтому здесь уничтожаем, хотя при вытеснении — нет
        _fonts?.Clear(disposeValues: true);
        _sizedFonts?.Clear(disposeValues: true);
    }

    public static SKFont Get(Font font)
    {
        GenerationalCache<Font, SKFont> fonts = Fonts;

        if (fonts.TryGet(font, out SKFont? cached))
            return cached!;

        SKTypeface typeface = ResolveTypeface(font);
        var skFont = new SKFont(typeface, font.Size);

        // Файл шрифта несёт ровно одно начертание, и SKTypeface.FromFile
        // подбирать по весу и наклону не умеет. Раньше Bold и Italic при
        // заданном FilePath просто игнорировались — жирный текст рисовался
        // обычным. Синтезируем: ключ кэша включает Weight и Style, так что
        // начертания разойдутся по разным SKFont на одном typeface.
        if (font.FilePath is not null)
        {
            if (font.Weight == FontWeight.Bold)
                skFont.Embolden = true;

            if (font.Style == FontStyle.Italic)
                skFont.SkewX = -0.25f;
        }

        fonts.Add(font, skFont);
        return skFont;
    }

    /// <summary>
    /// Сбросить подбор шрифтов целиком. Нужно там, где он меняется, —
    /// например, после подгрузки нового файла шрифта.
    /// </summary>
    /// <remarks>
    /// Typeface'ы намеренно не уничтожаются. Среди них лежит
    /// SKTypeface.Default, общий на процесс, а MatchFamily умеет вернуть
    /// подмену, из-за чего один объект оказывается сразу под несколькими
    /// ключами. Их единицы на сброс, и цена ошибки здесь несопоставима
    /// с выигрышем.
    ///
    /// Звать можно только когда отрисовка не идёт: потоковые SKFont
    /// освобождаются сразу, а на них могут ссылаться уже полученные
    /// вызывающей стороной CachedLine.
    /// </remarks>
    public static void Invalidate()
    {
        lock (Sync)
        {
            FileTypefaces.Clear();
            Typefaces.Clear();
        }

        Fallbacks.Clear();

        Interlocked.Increment(ref _version);

        // текущий поток чистится сразу, остальные — при первом обращении
        SyncVersion();
    }

    /// <summary>Сбросить разобранные строки. Нужно там, где меняется
    /// подбор шрифтов, — например, после подгрузки нового файла шрифта.</summary>
    /// <remarks>Оставлен ради вызывающего кода. Сбрасывать одни строки
    /// недостаточно: SKFont и typeface пережили бы сброс, и подбор
    /// остался бы прежним. Поэтому делает полный сброс.</remarks>
    public static void InvalidateLines() => Invalidate();

    /// <summary>
    /// Разбор строки на отрезки вместе с габаритами. Результат кэшируется:
    /// и раскладка, и отрисовка спрашивают об одной и той же строке
    /// по многу раз за кадр, а разбор стоит прохода по всем символам.
    /// </summary>
    public static CachedLine GetLine(string text, Font font)
    {
        if (string.IsNullOrEmpty(text))
            return CachedLine.Empty;

        GenerationalCache<(string Text, Font Font), CachedLine> lines = Lines;

        var key = (text, font);

        if (lines.TryGet(key, out CachedLine? cached))
            return cached!;

        CachedLine line = BuildLine(text, font);
        lines.Add(key, line);

        return line;
    }

    private static CachedLine BuildLine(string text, Font font)
    {
        SKFont primary = Get(font);

        // Быстрый путь: вся строка покрыта основным шрифтом. Один вызов
        // на строку вместо проверки глифа на каждый символ — для латиницы
        // и кириллицы это попадание всегда.
        bool wholeLineIsPrimary = primary.ContainsGlyphs(text);

        FontRun[] runs = wholeLineIsPrimary
            ? [new FontRun(0, text.Length, primary, 0)]
            : BuildMixedRuns(text, font);

        float width = 0;
        float height = 0;
        SKRect bounds = SKRect.Empty;

        for (int i = 0; i < runs.Length; i++)
        {
            FontRun run = runs[i];
            ReadOnlySpan<char> span = text.AsSpan(run.Start, run.Length);

            float advance = run.Font.MeasureText(span, out SKRect runBounds);

            runs[i] = new FontRun(run.Start, run.Length, run.Font, advance);

            width += advance;

            // высота — максимум высоты чернил, ровно как считалось раньше
            height = Math.Max(height, runBounds.Height);

            // на быстром пути единственный отрезок — это вся строка,
            // измеренная основным шрифтом. Отдельный замер ниже дал бы
            // тот же прямоугольник, поэтому берём уже посчитанный
            if (wholeLineIsPrimary)
                bounds = runBounds;
        }

        // на смешанном пути границы по-прежнему считаются основным шрифтом:
        // на этой величине стоят эталонные снимки
        if (!wholeLineIsPrimary)
            primary.MeasureText(text.AsSpan(), out bounds);

        return new CachedLine
        {
            Text = text,
            Runs = runs,
            Width = width,
            Height = height,
            Bounds = bounds,
        };
    }

    /// <summary>Медленный путь: в строке есть символы вне основного шрифта,
    /// поэтому подбор идёт посимвольно.</summary>
    private static FontRun[] BuildMixedRuns(string text, Font font)
    {
        float size = font.Size;

        var segments = new List<FontRun>();

        int start = 0;
        int position = 0;
        SKTypeface? currentTypeface = null;

        foreach (Rune rune in text.EnumerateRunes())
        {
            SKTypeface typeface = Resolve(font, rune.Value);

            if (currentTypeface is null)
            {
                currentTypeface = typeface;
            }
            else if (typeface.FamilyName != currentTypeface.FamilyName)
            {
                segments.Add(new FontRun(start, position - start, GetSized(currentTypeface, size), 0));
                start = position;
                currentTypeface = typeface;
            }

            position += rune.Utf16SequenceLength;
        }

        if (currentTypeface is not null && start < text.Length)
            segments.Add(new FontRun(start, text.Length - start, GetSized(currentTypeface, size), 0));

        return [.. segments];
    }

    /// <summary>
    /// Ширина начала строки длиной length символов. Подстрока не создаётся:
    /// целые отрезки берутся из уже посчитанных ширин, и меряется только
    /// хвостовой кусок. Метод зовётся на каждое положение каретки,
    /// поэтому аллокаций в нём быть не должно.
    /// </summary>
    public static float MeasurePrefix(string text, int length, Font font)
    {
        CachedLine line = GetLine(text, font);

        float width = 0;

        foreach (FontRun run in line.Runs)
        {
            if (run.Start >= length)
                break;

            int available = Math.Min(run.Length, length - run.Start);

            width += available == run.Length
                ? run.Width
                : run.Font.MeasureText(text.AsSpan(run.Start, available));
        }

        return width;
    }

    private static SKTypeface ResolveTypeface(Font font)
    {
        // Раньше метод звался из Get под общей блокировкой. Теперь словари
        // шрифтов потоковые и в замке не нуждаются, а разделяемыми остались
        // только typeface'ы — блокировка переехала сюда, к ним.
        lock (Sync)
        {
            if (font.FilePath is not null)
            {
                if (FileTypefaces.TryGetValue(font.FilePath, out SKTypeface? fromFile))
                    return fromFile;

                SKTypeface? loaded = SKTypeface.FromFile(font.FilePath);

                if (loaded is not null)
                {
                    FileTypefaces[font.FilePath] = loaded;
                    return loaded;
                }
            }

            var key = (font.Family, font.Weight, font.Style);

            if (Typefaces.TryGetValue(key, out SKTypeface? cached))
                return cached;

            var style = new SKFontStyle(
                font.Weight == FontWeight.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                font.Style == FontStyle.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

            SKTypeface? resolved = null;

            foreach (string raw in font.Family.Split(','))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;

                if (IsGeneric(name))
                {
                    resolved = SKFontManager.Default.MatchFamily(GenericToConcrete(name), style);
                    if (resolved is not null) break;
                    continue;
                }

                SKTypeface? candidate = SKFontManager.Default.MatchFamily(name, style);

                // MatchFamily может вернуть подмену вместо null, если семейства нет —
                // поэтому проверяем, что это действительно запрошенный шрифт
                if (candidate is not null &&
                    candidate.FamilyName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    resolved = candidate;
                    break;
                }
            }

            resolved ??= SKTypeface.Default;
            Typefaces[key] = resolved;
            return resolved;
        }
    }

    /// <summary>Шрифт, в котором есть глиф для символа: сначала основной,
    /// потом системная подстановка. Результат кэшируется — MatchCharacter
    /// каждый раз создаёт новый объект и заметно стоит.</summary>
    public static SKTypeface Resolve(Font font, int codepoint)
    {
        SKFont primary = Get(font);

        // проверка глифа переехала с SKTypeface на SKFont:
        // SKTypeface.ContainsGlyph объявлен устаревшим.
        // Вне замка это безопасно: primary принадлежит текущему потоку
        if (primary.ContainsGlyph(codepoint))
            return primary.Typeface;

        var key = (font.Family, font.Weight, font.Style, codepoint);

        // У кэша есть собственная блокировка, но общий замок здесь всё
        // равно нужен: без него два потока на одном промахе позвали бы
        // MatchCharacter дважды и завели два шрифта вместо одного
        lock (Sync)
        {
            if (Fallbacks.TryGet(key, out FallbackResult? cached))
                return cached!.Typeface ?? primary.Typeface;

            SKTypeface? found = SKFontManager.Default.MatchCharacter(codepoint);

            Fallbacks.Add(key, found is null ? FallbackResult.None : new FallbackResult(found));

            return found ?? primary.Typeface;
        }
    }

    public static SKFont GetSized(SKTypeface typeface, float size)
    {
        GenerationalCache<(SKTypeface, float), SKFont> sized = SizedFonts;

        var key = (typeface, size);

        if (sized.TryGet(key, out SKFont? cached))
            return cached!;

        var created = new SKFont(typeface, size);
        sized.Add(key, created);
        return created;
    }

    /// <summary>Разбивает строку на отрезки с одинаковым шрифтом.</summary>
    /// <remarks>Совместимая обёртка над кэшем разбора: подстроки здесь
    /// всё ещё создаются, но сам разбор берётся готовым. Отрисовка
    /// перейдёт на индексы отдельным шагом.</remarks>
    internal static IEnumerable<(string Text, SKFont Font)> SplitRuns(string text, Font font)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        CachedLine line = GetLine(text, font);

        foreach (FontRun run in line.Runs)
        {
            // отрезок на всю строку отдаём как есть: копия была бы лишней
            yield return run.Start == 0 && run.Length == text.Length
                ? (text, run.Font)
                : (text.Substring(run.Start, run.Length), run.Font);
        }
    }

    private static bool IsGeneric(string name) =>
        name is "sans-serif" or "serif" or "monospace";

    private static string GenericToConcrete(string generic) => generic switch
    {
        "serif" => "Times New Roman",
        "monospace" => "Consolas",
        _ => "Segoe UI",
    };
}