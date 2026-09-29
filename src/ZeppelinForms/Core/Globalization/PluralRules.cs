using System.Globalization;

namespace ZeppelinForms.Core.Globalization;

/// <summary>A plural category of CLDR. Which ones a language uses is up to the
/// language: English has One and Other, Russian — One, Few and Many.</summary>
public enum PluralCategory
{
    Zero,
    One,
    Two,
    Few,
    Many,
    Other,
}

/// <summary>Which plural form a count takes in a language.</summary>
/// <remarks>
/// Integer counts only — what interfaces actually count: files, rows, items.
/// The built-in rules follow CLDR for the languages listed below; any other
/// language falls back to the English rule, and one can be registered for it
/// with <see cref="Register"/>.
/// </remarks>
public static class PluralRules
{
    private static readonly Dictionary<string, Func<long, PluralCategory>> Custom =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Lock Sync = new();

    /// <summary>Set the rule for a language, by its two-letter ISO code ("lt", "ar").
    /// Overrides the built-in one.</summary>
    public static void Register(string language, Func<long, PluralCategory> rule)
    {
        lock (Sync)
            Custom[language] = rule;
    }

    public static PluralCategory Select(CultureInfo culture, long count)
    {
        string language = culture.TwoLetterISOLanguageName;

        lock (Sync)
        {
            if (Custom.TryGetValue(language, out Func<long, PluralCategory>? rule))
                return rule(count);
        }

        // the sign doesn't change the form: "-5 files" as well as "5 files"
        long n = Math.Abs(count);

        return language switch
        {
            // one for 0 and 1
            "fr" or "pt" or "hy" => n is 0 or 1 ? PluralCategory.One : PluralCategory.Other,

            // 1, 21, 31 — one; 2–4, 22–24 — few; the rest, including 11–14, — many
            "ru" or "uk" or "be" => EastSlavic(n),

            // only exactly 1 is one; the rest as in Russian
            "pl" => n == 1 ? PluralCategory.One
                : IsFew(n) ? PluralCategory.Few
                : PluralCategory.Many,

            // 2–4 are few, and only those — 22 is already other
            "cs" or "sk" => n == 1 ? PluralCategory.One
                : n is >= 2 and <= 4 ? PluralCategory.Few
                : PluralCategory.Other,

            // no plural at all
            "ja" or "zh" or "ko" or "vi" or "th" or "id" or "ms" => PluralCategory.Other,

            // English and most European languages: one for exactly 1
            _ => n == 1 ? PluralCategory.One : PluralCategory.Other,
        };
    }

    private static PluralCategory EastSlavic(long n)
    {
        if (n % 10 == 1 && n % 100 != 11) return PluralCategory.One;

        return IsFew(n) ? PluralCategory.Few : PluralCategory.Many;
    }

    private static bool IsFew(long n) =>
        n % 10 is >= 2 and <= 4 && n % 100 is not (>= 12 and <= 14);
}