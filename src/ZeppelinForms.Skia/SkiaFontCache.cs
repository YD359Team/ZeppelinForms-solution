using SkiaSharp;
using System.Text;
using ZeppelinForms.Drawing;

namespace ZeppelinForms.Skia;

internal static class SkiaFontCache
{
    private static readonly Dictionary<Font, SKFont> Fonts = [];
    private static readonly Dictionary<string, SKTypeface> FileTypefaces = [];
    private static readonly Dictionary<(string, FontWeight, FontStyle), SKTypeface> Typefaces = [];
    private static readonly Dictionary<(string, FontWeight, FontStyle, int), SKTypeface?> Fallbacks = [];
    private static readonly Lock Sync = new();

    /// <summary>
    /// Разобранные строки. Лимит поколения подобран под интерфейс:
    /// одновременно на экране редко бывает больше нескольких сотен
    /// различных строк, а всё сверх того — ввод в поле, который
    /// устаревает сам.
    /// </summary>
    private static readonly GenerationalCache<(string Text, Font Font), CachedLine> Lines = new(4096);

    public static SKFont Get(Font font)
    {
        lock (Sync)
        {
            if (Fonts.TryGetValue(font, out SKFont? cached))
                return cached;

            SKTypeface typeface = ResolveTypeface(font);
            var skFont = new SKFont(typeface, font.Size);

            Fonts[font] = skFont;
            return skFont;
        }
    }

    /// <summary>Сбросить разобранные строки. Нужно там, где меняется
    /// подбор шрифтов, — например, после подгрузки нового файла шрифта.</summary>
    public static void InvalidateLines() => Lines.Clear();

    /// <summary>
    /// Разбор строки на отрезки вместе с габаритами. Результат кэшируется:
    /// и раскладка, и отрисовка спрашивают об одной и той же строке
    /// по многу раз за кадр, а разбор стоит прохода по всем символам.
    /// </summary>
    public static CachedLine GetLine(string text, Font font)
    {
        if (string.IsNullOrEmpty(text))
            return CachedLine.Empty;

        var key = (text, font);

        if (Lines.TryGet(key, out CachedLine? cached))
            return cached!;

        CachedLine line = BuildLine(text, font);
        Lines.Add(key, line);

        return line;
    }

    private static CachedLine BuildLine(string text, Font font)
    {
        SKFont primary = Get(font);

        // Быстрый путь: вся строка покрыта основным шрифтом. Один вызов
        // на строку вместо проверки глифа на каждый символ — для латиницы
        // и кириллицы это попадание всегда.
        FontRun[] runs = primary.ContainsGlyphs(text)
            ? [new FontRun(0, text.Length, primary, 0)]
            : BuildMixedRuns(text, font);

        float width = 0;
        float height = 0;

        for (int i = 0; i < runs.Length; i++)
        {
            FontRun run = runs[i];
            ReadOnlySpan<char> span = text.AsSpan(run.Start, run.Length);

            float advance = run.Font.MeasureText(span, out SKRect runBounds);

            runs[i] = new FontRun(run.Start, run.Length, run.Font, advance);

            width += advance;

            // высота — максимум высоты чернил, ровно как считалось раньше
            height = Math.Max(height, runBounds.Height);
        }

        primary.MeasureText(text.AsSpan(), out SKRect bounds);

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
        // Вызывается только из Get, а он уже под блокировкой:
        // словари типов не потокобезопасны, и раньше часть обращений
        // к Typefaces шла мимо замка.
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

    /// <summary>Шрифт, в котором есть глиф для символа: сначала основной,
    /// потом системная подстановка. Результат кэшируется — MatchCharacter
    /// каждый раз создаёт новый объект и заметно стоит.</summary>
    public static SKTypeface Resolve(Font font, int codepoint)
    {
        SKFont primary = Get(font);

        // проверка глифа переехала с SKTypeface на SKFont:
        // SKTypeface.ContainsGlyph объявлен устаревшим
        if (primary.ContainsGlyph(codepoint))
            return primary.Typeface;

        var key = (font.Family, font.Weight, font.Style, codepoint);

        lock (Sync)
        {
            if (Fallbacks.TryGetValue(key, out SKTypeface? cached))
                return cached ?? primary.Typeface;

            SKTypeface? found = SKFontManager.Default.MatchCharacter(codepoint);
            Fallbacks[key] = found;
            return found ?? primary.Typeface;
        }
    }

    public static SKFont GetSized(SKTypeface typeface, float size)
    {
        var key = (typeface, size);

        lock (Sync)
        {
            if (SizedFonts.TryGetValue(key, out SKFont? cached))
                return cached;

            var created = new SKFont(typeface, size);
            SizedFonts[key] = created;
            return created;
        }
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

    private static readonly Dictionary<(SKTypeface, float), SKFont> SizedFonts = [];

    private static bool IsGeneric(string name) =>
        name is "sans-serif" or "serif" or "monospace";

    private static string GenericToConcrete(string generic) => generic switch
    {
        "serif" => "Times New Roman",
        "monospace" => "Consolas",
        _ => "Segoe UI",
    };
}