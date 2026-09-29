namespace ZeppelinForms.Core.Globalization;

/// <summary>The strings of one language. Written in C# rather than .resx:
/// a table is an ordinary class, it is refactored, searched and checked
/// by the compiler together with the code.</summary>
/// <example><code>
/// public sealed class GermanStrings : StringTable
/// {
///     public GermanStrings() : base("de")
///     {
///         Add(AppText.Save, "Speichern");
///         Add(AppText.Files, one: "{0} Datei", other: "{0} Dateien");
///     }
/// }
///
/// Localization.Register(new GermanStrings());
/// </code></example>
public class StringTable(string cultureName)
{
    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluralForms> _plurals = new(StringComparer.Ordinal);

    /// <summary>The culture the table is for: a neutral one ("ru") covers every
    /// region of the language, a specific one ("ru-RU") only that region and is
    /// looked up first.</summary>
    public string CultureName { get; } = cultureName.Trim();

    public StringTable Add(TextKey key, string text)
    {
        _texts[key.Id] = text;
        return this;
    }

    /// <summary>The forms of a plural text. Give the ones the language uses;
    /// a missing form falls back to other, then to many, then to one.</summary>
    public StringTable Add(
        PluralKey key,
        string? one = null,
        string? few = null,
        string? many = null,
        string? other = null,
        string? zero = null,
        string? two = null)
    {
        if (one is null && few is null && many is null && other is null && zero is null && two is null)
            throw new ArgumentException($"No forms given for '{key.Id}'.", nameof(key));

        _plurals[key.Id] = new PluralForms(zero, one, two, few, many, other);
        return this;
    }

    internal bool TryGet(TextKey key, out string text) =>
        _texts.TryGetValue(key.Id, out text!);

    internal bool TryGet(PluralKey key, PluralCategory category, out string text)
    {
        if (!_plurals.TryGetValue(key.Id, out PluralForms? forms))
        {
            text = string.Empty;
            return false;
        }

        text = forms.For(category);
        return true;
    }

    private sealed record PluralForms(
        string? Zero, string? One, string? Two, string? Few, string? Many, string? Other)
    {
        public string For(PluralCategory category)
        {
            string? exact = category switch
            {
                PluralCategory.Zero => Zero,
                PluralCategory.One => One,
                PluralCategory.Two => Two,
                PluralCategory.Few => Few,
                PluralCategory.Many => Many,
                _ => Other,
            };

            // at least one form is guaranteed by Add
            return exact ?? Other ?? Many ?? One ?? Few ?? Two ?? Zero!;
        }
    }
}