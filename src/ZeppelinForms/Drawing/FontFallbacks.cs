namespace ZeppelinForms.Drawing;

/// <summary>
/// Font files a character is looked for in when the font being drawn with has no
/// glyph for it — before the system's own fallback.
/// </summary>
/// <remarks>
/// <para>
/// On a desktop a missing character is found in the system's fonts: Hebrew, Arabic,
/// CJK and emoji come from whatever the system has. A browser gives Skia no fonts at
/// all, and there a character the one font of the application lacks was drawn as an
/// empty box — the Hebrew and Arabic of the text input showcase in the WASM example.
/// The files added here are searched first, in the order added, on every platform:
/// on a desktop they make the text look the same everywhere, in the browser they
/// are the only fallback there is.
/// </para>
/// <para>
/// In the browser the files are downloaded by <c>BrowserApp.RunAsync</c>: add them
/// before calling it. Elsewhere a path is a file on disk.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// FontFallbacks.Add("/fonts/NotoSansHebrew-Regular.ttf");
/// FontFallbacks.Add("/fonts/NotoSansArabic-Regular.ttf");
/// </code>
/// </example>
public static class FontFallbacks
{
    private static readonly Lock s_lock = new();

    /// <summary>Replaced whole on every change: a reader takes the array once and
    /// sees one consistent list, and the text backend notices a change by reference.</summary>
    private static volatile string[] s_paths = [];

    /// <summary>The fallback files, in the order they are searched.</summary>
    public static IReadOnlyList<string> Paths => s_paths;

    /// <summary>Add a font file to search after the ones added before it. A path
    /// added already is not added twice.</summary>
    /// <exception cref="ArgumentException">An empty path.</exception>
    public static void Add(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        lock (s_lock)
        {
            if (Array.IndexOf(s_paths, path) >= 0) return;

            s_paths = [.. s_paths, path];
        }
    }

    /// <summary>Remove every fallback file: only the system's fallback is left.</summary>
    public static void Clear()
    {
        lock (s_lock)
            s_paths = [];
    }
}