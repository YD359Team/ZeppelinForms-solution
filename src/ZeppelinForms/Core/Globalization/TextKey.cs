namespace ZeppelinForms.Core.Globalization;

/// <summary>A localizable text. The English text inside is the last fallback:
/// without any table registered, the application still shows something sensible.</summary>
/// <remarks>
/// A key is an object rather than a string, so that a typo in it is a compile
/// error rather than a text that silently never gets translated. The Id is what
/// tables are looked up by; keep it stable — it is the key's identity, the English
/// text may change freely.
/// </remarks>
/// <example><code>
/// public static readonly TextKey Save = new("app.save", "Save");
/// public static readonly TextKey Greeting = new("app.greeting", "Hello, {0}!");
/// </code></example>
public sealed class TextKey(string id, string defaultText)
{
    public string Id { get; } = id;

    /// <summary>The English text. May contain {0}-style placeholders.</summary>
    public string DefaultText { get; } = defaultText;

    public override string ToString() => Id;
}

/// <summary>A localizable text whose form depends on a count: "1 file", "5 files".
/// The count is always placeholder {0}.</summary>
/// <remarks>
/// The English forms inside are the fallback, chosen by the English rule —
/// one for exactly one, other for everything else. Tables of other languages
/// give the forms their language needs: Russian has one, few and many.
/// </remarks>
public sealed class PluralKey(string id, string one, string other)
{
    public string Id { get; } = id;

    public string One { get; } = one;

    public string Other { get; } = other;

    internal string DefaultFor(long count) => Math.Abs(count) == 1 ? One : Other;

    public override string ToString() => Id;
}