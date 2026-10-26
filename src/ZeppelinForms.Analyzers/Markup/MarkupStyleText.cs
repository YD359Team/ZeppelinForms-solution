using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ZeppelinForms.Analyzers.Markup;

/// <summary>A problem in the styles of markup: where it is, and either the message of a
/// style that can't be read or — <see cref="Misplaced"/> — an element where none goes.</summary>
internal readonly struct MarkupStyleIssue(XObject at, string message, bool misplaced, string holder, string child)
{
    public XObject At { get; } = at;

    public string Message { get; } = message;

    /// <summary>An element or text where none goes: <see cref="Holder"/> can't hold
    /// <see cref="Child"/>, and <see cref="Message"/> says what it holds.</summary>
    public bool Misplaced { get; } = misplaced;

    public string Holder { get; } = holder;

    public string Child { get; } = child;
}

/// <summary>
/// The styles of markup — <c>&lt;Style Selector="…"&gt;</c> with <c>&lt;Setter&gt;</c>,
/// <c>&lt;Transition&gt;</c> and nested styles — as the text of a style sheet, each
/// statement on the line and at the column of its element.
/// </summary>
/// <remarks>
/// <para>
/// One file for both of its readers: the markup generator, which checks the text and
/// builds the sheet from it at run time, and the previewer's host, which parses the same
/// text with the framework's own parser and so checks the values too — the build can't,
/// their grammar is the style sheets'. As the lines are those of the markup, what either
/// of them reports points at the element that wrote it.
/// </para>
/// <para>
/// <c>^</c> of a nested selector is the style sheets' <c>&amp;</c>, the enclosing
/// selector; a nested selector without it is a descendant, as in a sheet.
/// </para>
/// </remarks>
internal static class MarkupStyleText
{
    public const string DefaultNamespace = "https://zeppelinforms.dev/markup";

    /// <summary>The text of the styles, one sheet; empty when there is nothing to apply.
    /// What can't be written goes to <paramref name="report"/> and is left out.</summary>
    public static string Write(IEnumerable<XElement> styles, Action<MarkupStyleIssue> report)
    {
        var sheet = new SheetText();

        foreach (XElement style in styles)
            WriteStyle(sheet, style, nested: false, report);

        string text = sheet.ToString();

        return text.Trim().Length == 0 ? string.Empty : text;
    }

    /// <summary>Every sheet the markup's <c>&lt;Type.Styles&gt;</c> make — the styles
    /// between two <c>&lt;StyleInclude&gt;</c>s are one — with what can't be written
    /// left out. For the previewer's host, which checks them as sheets.</summary>
    public static List<string> SheetsOf(XDocument document)
    {
        var sheets = new List<string>();

        foreach (XElement holder in document.Descendants().Where(IsStylesProperty))
        {
            var styles = new List<XElement>();

            foreach (XElement child in holder.Elements())
            {
                if (IsElement(child, "Style"))
                {
                    styles.Add(child);
                    continue;
                }

                Flush();
            }

            Flush();

            void Flush()
            {
                if (styles.Count == 0) return;

                string text = Write(styles, _ => { });
                if (text.Length > 0) sheets.Add(text);

                styles.Clear();
            }
        }

        return sheets;
    }

    /// <summary>&lt;Type.Styles&gt;: a property element of the framework's namespace
    /// named Styles.</summary>
    public static bool IsStylesProperty(XElement element) =>
        element.Name.NamespaceName == DefaultNamespace &&
        element.Name.LocalName.EndsWith(".Styles", StringComparison.Ordinal) &&
        element.Name.LocalName.IndexOf('.') == element.Name.LocalName.Length - ".Styles".Length;

    public static bool IsElement(XElement element, string name) =>
        element.Name.NamespaceName == DefaultNamespace && element.Name.LocalName == name;

    /// <summary>&lt;Style Selector="…"&gt; as a rule: its setters, transitions and nested
    /// styles inside, '^' of a nested selector standing for the enclosing one.</summary>
    private static void WriteStyle(SheetText sheet, XElement style, bool nested, Action<MarkupStyleIssue> report)
    {
        string selector = ((string?)style.Attribute("Selector"))?.Trim() ?? string.Empty;

        if (selector.Length == 0)
        {
            report(Bad(style, "A Style needs a Selector: <Style Selector=\"Button.primary\">"));
            return;
        }

        if (!nested && selector.IndexOf('^') >= 0)
        {
            report(Bad(style, "'^' stands for the selector of the enclosing Style, and this one is in none"));
            return;
        }

        if (style.Attributes().FirstOrDefault(a => !a.IsNamespaceDeclaration && a.Name.LocalName != "Selector") is { } stray)
        {
            report(Bad(stray, "A Style takes only a Selector; '" + stray.Name.LocalName + "' is a Setter's"));
            return;
        }

        sheet.Put(style, selector.Replace("^", "&") + " {");

        foreach (XNode child in style.Nodes())
        {
            switch (child)
            {
                case XElement element when IsElement(element, "Setter"):
                    WriteSetter(sheet, element, report);
                    break;

                case XElement element when IsElement(element, "Transition"):
                    WriteTransition(sheet, element, report);
                    break;

                case XElement element when IsElement(element, "Style"):
                    WriteStyle(sheet, element, nested: true, report);
                    break;

                case XElement element:
                    report(new MarkupStyleIssue(element, "it holds <Setter>, <Transition> and nested <Style>",
                        misplaced: true, "Style", element.Name.LocalName));
                    break;

                case XText text when !string.IsNullOrWhiteSpace(text.Value):
                    report(new MarkupStyleIssue(text, "it holds <Setter>, <Transition> and nested <Style>",
                        misplaced: true, "Style", "text"));
                    break;
            }
        }

        sheet.Close();
    }

    /// <summary>&lt;Setter Property="Padding" Value="14 6"/&gt; — or the value as the
    /// element's text — in the style sheets' own values: theme references, functions.</summary>
    private static void WriteSetter(SheetText sheet, XElement setter, Action<MarkupStyleIssue> report)
    {
        string property = ((string?)setter.Attribute("Property"))?.Trim() ?? string.Empty;
        string? value = ((string?)setter.Attribute("Value") ?? (setter.HasElements ? null : setter.Value))?.Trim();

        if (property.Length == 0)
        {
            report(Bad(setter, "A Setter names its Property: <Setter Property=\"Padding\" Value=\"14 6\"/>"));
            return;
        }

        if (setter.HasElements)
        {
            report(Bad(setter, "A Setter's value is text, written as the style sheets write it: Value=\"14 6\""));
            return;
        }

        if (value is null || value.Length == 0)
        {
            report(Bad(setter, property + " has no Value"));
            return;
        }

        if (!IsPlainValue(value))
        {
            report(Bad(setter, "A value can't hold ';', '{' or '}': '" + value + "'"));
            return;
        }

        sheet.Put(setter, property + ": " + value + ";");
    }

    /// <summary>&lt;Transition Property="Background" Duration="150ms" Easing="ease-out"/&gt;.</summary>
    private static void WriteTransition(SheetText sheet, XElement transition, Action<MarkupStyleIssue> report)
    {
        string property = ((string?)transition.Attribute("Property"))?.Trim() ?? string.Empty;
        string duration = ((string?)transition.Attribute("Duration"))?.Trim() ?? string.Empty;
        string easing = ((string?)transition.Attribute("Easing"))?.Trim() ?? string.Empty;

        if (property.Length == 0 || duration.Length == 0)
        {
            report(Bad(transition,
                "A Transition names its Property and Duration: <Transition Property=\"Background\" Duration=\"150ms\"/>"));
            return;
        }

        if (!IsPlainValue(property + " " + duration + " " + easing) || property.IndexOf(' ') >= 0 || duration.IndexOf(' ') >= 0)
        {
            report(Bad(transition, "A Transition's Property, Duration and Easing are single words"));
            return;
        }

        sheet.Put(transition, "transition: " + property + " " + duration + (easing.Length > 0 ? " " + easing : string.Empty) + ";");
    }

    private static MarkupStyleIssue Bad(XObject at, string message) =>
        new(at, message, misplaced: false, string.Empty, string.Empty);

    /// <summary>The value can't end the declaration or open a block: no ';', '{' or '}'
    /// outside quotes.</summary>
    private static bool IsPlainValue(string value)
    {
        char quote = '\0';

        foreach (char c in value)
        {
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }

            if (c is '"' or '\'') quote = c;
            else if (c is ';' or '{' or '}') return false;
        }

        return true;
    }

    /// <summary>The text of a style sheet built from markup: each statement on the line
    /// and at the column of its element.</summary>
    private sealed class SheetText
    {
        private readonly List<StringBuilder> _lines = [];
        private int _last;

        public void Put(XObject at, string statement)
        {
            IXmlLineInfo info = at;
            int line = info.HasLineInfo() ? info.LineNumber : Math.Max(_last, 1);
            int column = info.HasLineInfo() ? info.LinePosition : 1;

            // a statement never goes above the one before it: the order is the cascade's
            line = Math.Max(line, _last);

            while (_lines.Count < line) _lines.Add(new StringBuilder());

            StringBuilder text = _lines[line - 1];

            if (text.Length == 0) text.Append(' ', Math.Max(0, column - 1));
            else text.Append(' ');

            text.Append(statement);
            _last = line;
        }

        /// <summary>The end of a rule: on the line of what it held last.</summary>
        public void Close()
        {
            if (_lines.Count == 0) return;

            _lines[Math.Max(_last, 1) - 1].Append(" }");
        }

        public override string ToString() => string.Join("\n", _lines.Select(l => l.ToString()));
    }
}