using System.Diagnostics;

namespace ZeppelinForms.Forms.Styling;

/// <summary>
/// Styles written as text, in a <c>.zss</c> file — a small dialect of CSS over the
/// framework's own controls and properties:
/// </summary>
/// <example>
/// <code>
/// $radius: 6;
///
/// Button.primary {
///     BackgroundColor: @Accent;
///     TextColor: @TextOnAccent;
///     CornerRadius: $radius;
///     transition: BackgroundColor 150ms ease-out;
///
///     &amp;:hover { BackgroundColor: @AccentHover; }
///     &amp;:pressed { BackgroundColor: @AccentPressed; }
/// }
///
/// StackPanel.list &gt; Label:odd { Background: alpha(@Accent, 0.08); }
/// </code>
/// </example>
/// <remarks>
/// <para>
/// Selectors are those of <see cref="Selector"/>. Property names are the C# names —
/// <c>BackgroundColor</c> — or the same in kebab case — <c>background-color</c>.
/// Values are written the way the C# would construct them: <c>Padding: 14 6</c> is
/// <c>new Thickness(14, 6)</c>. See <see cref="StyleValueCompiler"/> for colors,
/// theme references (<c>@Accent</c>, <c>@Metrics.ControlCornerRadius</c>) and functions.
/// </para>
/// <para>
/// Besides rules there are <c>$name: value;</c> variables, <c>@import "file.zss";</c>
/// relative to the importing file, nested rules with <c>&amp;</c> standing for the
/// enclosing selector (a nested selector without <c>&amp;</c> is a descendant), and
/// <c>transition: Property duration [easing], …</c>. Comments are <c>/* … */</c>
/// and <c>// …</c>.
/// </para>
/// <para>
/// Mistakes don't stop the sheet. As in CSS, a declaration that can't be read is
/// skipped, a rule with a broken selector is dropped, and the rest applies; each
/// problem is reported in <see cref="Diagnostics"/> with its file, line and column, and
/// written to the debug output in the form an IDE turns into a link.
/// </para>
/// </remarks>
public sealed class StyleSheet
{
    internal StyleSheet(string? path, IReadOnlyList<Style> styles, IReadOnlyList<StyleDiagnostic> diagnostics, IReadOnlyList<string> files)
    {
        Path = path;
        Styles = styles;
        Diagnostics = diagnostics;
        Files = files;
    }

    /// <summary>The file the sheet was loaded from; null for a sheet parsed from text.</summary>
    public string? Path { get; }

    /// <summary>The rules, in the order of the text, imports in place.</summary>
    public IReadOnlyList<Style> Styles { get; }

    public IReadOnlyList<StyleDiagnostic> Diagnostics { get; }

    /// <summary>Every file the sheet was read from: itself and its imports. A watched
    /// sheet reloads when any of them changes.</summary>
    public IReadOnlyList<string> Files { get; }

    public bool HasErrors => Diagnostics.Any(d => d.Severity == StyleDiagnosticSeverity.Error);

    /// <summary>Raised for every sheet parsed with problems, wherever it came from:
    /// a place to show them — a debug overlay, a log, a test.</summary>
    public static event EventHandler<StyleSheet>? DiagnosticsReported;

    /// <summary>Parse a sheet from text.</summary>
    /// <param name="sourceName">How diagnostics and the inspector call the text.</param>
    /// <param name="baseDirectory">Where <c>@import</c> looks for files; the current
    /// directory if null.</param>
    public static StyleSheet Parse(string text, string? sourceName = null, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var parser = new StyleSheetParser(baseDirectory ?? Environment.CurrentDirectory);
        parser.ParseText(text, sourceName ?? "<style sheet>");

        return Finish(null, parser);
    }

    /// <summary>Load a sheet from a file.</summary>
    public static StyleSheet Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string full = System.IO.Path.GetFullPath(path);

        var parser = new StyleSheetParser(System.IO.Path.GetDirectoryName(full)!);
        parser.ParseFile(full);

        return Finish(full, parser);
    }

    /// <summary>Load a sheet from the application's assets folder, the way
    /// <see cref="Drawing.Imaging.Image"/> assets are found: <see cref="Assets.Root"/>
    /// on the desktop, the preloaded virtual file system in the browser.</summary>
    public static StyleSheet LoadAsset(string relativePath) =>
        Load(System.IO.Path.Combine(Assets.Root, relativePath));

    private static StyleSheet Finish(string? path, StyleSheetParser parser)
    {
        var sheet = new StyleSheet(path, parser.Styles, parser.Diagnostics, parser.Files);

        if (sheet.Diagnostics.Count > 0)
        {
            foreach (StyleDiagnostic diagnostic in sheet.Diagnostics)
                Debug.WriteLine(diagnostic.ToString());

            DiagnosticsReported?.Invoke(null, sheet);
        }

        return sheet;
    }
}

public enum StyleDiagnosticSeverity
{
    /// <summary>The sheet works, but probably not as meant: a type or a pseudo-class
    /// nobody has, so the selector never matches.</summary>
    Warning,

    /// <summary>Something was skipped: a declaration, a rule, an import.</summary>
    Error,
}

/// <summary>A problem in a style sheet, with its place.</summary>
public sealed record StyleDiagnostic(
    StyleDiagnosticSeverity Severity,
    string Message,
    string Source,
    int Line,
    int Column)
{
    /// <summary>In the form compilers use — <c>file(line,column): error: message</c> —
    /// which Visual Studio and Rider turn into a link in the output window.</summary>
    public override string ToString() =>
        $"{Source}({Line},{Column}): {(Severity == StyleDiagnosticSeverity.Error ? "error" : "warning")} ZSS: {Message}";
}