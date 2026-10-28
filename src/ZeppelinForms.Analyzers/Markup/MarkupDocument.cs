using System.Xml;
using System.Xml.Linq;

namespace ZeppelinForms.Analyzers.Markup;

/// <summary>
/// A markup file read as XML with its namespaces already declared: an element without a
/// prefix is the framework's, <c>x:</c> is the markup's own. A file needs no
/// <c>xmlns</c> lines of its own — <c>&lt;Form Class="App.MainForm"&gt;</c> is a whole
/// root — and one that has them, as XAML writes them, reads the same.
/// </summary>
/// <remarks>
/// One file for both of its readers: the markup generator and the previewer's host,
/// which checks the styles of the markup. Both must see the same elements in the same
/// namespaces, or the host would find no styles in a file the build compiles.
/// </remarks>
internal static class MarkupDocument
{
    public const string DefaultNamespace = "https://zeppelinforms.dev/markup";
    public const string XNamespace = "https://zeppelinforms.dev/markup/x";

    /// <summary>The markup of a text. A text, not a path: the generator may not open
    /// files (RS1035), and the previewer's host reads the file itself.</summary>
    /// <exception cref="XmlException">The text is not well-formed XML.</exception>
    public static XDocument Parse(string text)
    {
        using var reader = new StringReader(text);

        var table = new NameTable();
        var namespaces = new XmlNamespaceManager(table);

        // declared as if the root had xmlns="…" and xmlns:x="…": a declaration the file
        // makes itself is nearer and wins
        namespaces.AddNamespace(string.Empty, DefaultNamespace);
        namespaces.AddNamespace("x", XNamespace);

        var context = new XmlParserContext(table, namespaces, null, XmlSpace.None);

        var settings = new XmlReaderSettings
        {
            NameTable = table,
            DtdProcessing = DtdProcessing.Prohibit,
        };

        using XmlReader xml = XmlReader.Create(reader, settings, context);

        return XDocument.Load(xml, LoadOptions.SetLineInfo);
    }
}