using ZeppelinForms.Core.Text;
using ZeppelinForms.Core.Text.Syntax;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;

namespace ZF_SharedLib.Showcase;

/// <summary>Rich text, code and paginated documents — the editors added in 0.21.</summary>
internal static class EditorsPage
{
    public static UIElement Create()
    {
        // ===== RichTextBox =====

        var rich = new RichTextBox
        {
            Text = "Select a word and use the toolbar: bold, italic, underline, or a colour.\n\n"
                 + "Ctrl+B, Ctrl+I and Ctrl+U toggle the common styles on the selection. "
                 + "Formatting is stored as spans beside the plain text.",
            Size = new(560, 160),
        };

        var bold = new Button { Text = "Bold" };
        bold.Click += (_, _) => rich.ToggleBold();

        var italic = new Button { Text = "Italic" };
        italic.Click += (_, _) => rich.ToggleItalic();

        var underline = new Button { Text = "Underline" };
        underline.Click += (_, _) => rich.ToggleUnderline();

        var red = new Button { Text = "Red" };
        red.Click += (_, _) => rich.SetForeground(new Color(255, 192, 57, 43));

        var accent = new Button { Text = "Accent" };
        accent.Click += (_, _) => rich.SetForeground(new Color(255, 0, 120, 215));

        var clearStyle = new Button { Text = "Clear style" };
        clearStyle.Click += (_, _) => rich.ApplyStyle(RichTextStyle.Empty);

        var richToolbar = Demo.Row(bold, italic, underline, red, accent, clearStyle);
        var richNote = Demo.Note("The plain Text property stays bindable; Document holds the spans.");

        // ===== CodeEdit =====

        var code = new CodeEdit
        {
            Text =
                """
                using System;

                namespace Demo;

                // Ctrl+D duplicates the line; click a line number to select it
                public class Greeter
                {
                    public static void Hello(string name)
                    {
                        if (string.IsNullOrEmpty(name))
                            name = "world";

                        Console.WriteLine($"Hello, {name}!");
                    }
                }
                """,
            SyntaxRules = SyntaxRuleSet.CSharp(),
            ShowLineNumbers = true,
            IsFoldingEnabled = true,
            DuplicateLineEnabled = true,
            Size = new(560, 240),
        };

        var lineNumbers = new ToggleSwitch { Text = "Line numbers", IsOn = true };
        lineNumbers.Toggled += (_, _) => code.ShowLineNumbers = lineNumbers.IsOn;

        var folding = new ToggleSwitch { Text = "Folding", IsOn = true };
        folding.Toggled += (_, _) =>
        {
            code.IsFoldingEnabled = folding.IsOn;
            if (!folding.IsOn)
                code.ExpandAllFolds();
        };

        var completion = new ToggleSwitch { Text = "Completion", IsOn = true };
        completion.Toggled += (_, _) => code.IsCompletionEnabled = completion.IsOn;

        var expandFolds = new Button { Text = "Expand folds" };
        expandFolds.Click += (_, _) => code.ExpandAllFolds();

        var highlightHello = new Button { Text = "Highlight \"Hello\"" };
        highlightHello.Click += (_, _) => code.HighlightOccurrences("Hello");

        var clearHighlight = new Button { Text = "Clear highlight" };
        clearHighlight.Click += (_, _) => code.HighlightOccurrences(null);

        var codeToolbar = Demo.Column(
            Demo.Row(lineNumbers, folding, completion),
            Demo.Row(expandFolds, highlightHello, clearHighlight));

        var codeNote = Demo.Note(
            "Highlighting is a trie of keywords plus literal blocks — not a stack of regexes. "
            + "Ctrl+Space opens completion from the rule set; Ctrl+D duplicates the line or selection.");

        // ===== DocumentEdit =====

        var document = new DocumentEdit
        {
            Text =
                "A paginated document.\n\n"
                + "Each page has margins, an optional header and footer, and a page number. "
                + "Change the paper or the orientation below; the text reflows across the sheets.\n\n"
                + "Print raises PrintRequested with the laid-out pages — the host talks to the "
                + "system printer. Ctrl+P does the same from the keyboard.\n\n"
                + string.Join("\n\n", Enumerable.Range(1, 8).Select(i =>
                    $"Section {i}. More body text so that the layout needs several pages on A4. "
                    + "The caret, selection and rich styles work as on RichTextBox.")),
            Size = new(560, 360),
        };
        document.SetHeader("ZeppelinForms · showcase");
        document.SetFooter("Editors");
        document.SetMargins(new Thickness(72));

        Label printStatus = Demo.Note("Print has not been requested yet.");
        document.PrintRequested += (_, req) =>
            printStatus.Text = $"Print requested: {req.Pages.Count} page(s), {req.PlainText.Length} characters.";

        var a4 = new Button { Text = "A4" };
        a4.Click += (_, _) => document.SetPaper(PageSize.A4);

        var letter = new Button { Text = "Letter" };
        letter.Click += (_, _) => document.SetPaper(PageSize.Letter);

        var portrait = new Button { Text = "Portrait" };
        portrait.Click += (_, _) => document.SetOrientation(PageOrientation.Portrait);

        var landscape = new Button { Text = "Landscape" };
        landscape.Click += (_, _) => document.SetOrientation(PageOrientation.Landscape);

        var print = new PrimaryButton { Text = "Print…" };
        print.Click += (_, _) => document.Print();

        var docToolbar = Demo.Column(
            Demo.Row(a4, letter, portrait, landscape, print),
            printStatus);

        var docNote = Demo.Note(
            "PageSettings covers paper, orientation, margins, header and footer. "
            + "Select text and apply Bold (Ctrl+B) as on the rich field above.");

        return Demo.Page(
            "Editors",
            "Rich text with character styles, a code editor with syntax and folds, and a paginated document.",
            Demo.Section("RichTextBox", Demo.Column(richToolbar, rich, richNote),
                "Character formatting lives in Document spans; Text stays the plain string for bindings"),
            Demo.Section("CodeEdit", Demo.Column(codeToolbar, code, codeNote),
                "Line numbers, folding, occurrence highlights, Ctrl+D and keyword completion"),
            Demo.Section("DocumentEdit", Demo.Column(docToolbar, document, docNote),
                "Word-processor pages: layout, headers/footers and PrintRequested"));
    }
}
