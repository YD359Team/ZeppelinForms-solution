using System.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.ExternalControlTests.Controls;

/// <summary>A one-line text field, written as a library writes an editor of its own:
/// it takes the focus, shows the input method's composition in place, tells the
/// input method where its caret is, and opens a flyout in its form.</summary>
/// <remarks>Fixed-width characters: the field measures nothing, the tests read the
/// caret's place from the length of the text.</remarks>
public partial class CodeField : InteractiveControl, ITextCompositionTarget
{
    public const float CharWidth = 8f;

    private const float Inset = 4f;

    private readonly StringBuilder _text = new();

    public string Text => _text.ToString();

    /// <summary>What the input method is composing, not part of the text yet.</summary>
    public string Composition { get; private set; } = string.Empty;

    public int CompositionCursor { get; private set; }

    /// <summary>The caret, in the field's coordinates: after the text and the
    /// composed part up to the input method's cursor.</summary>
    public Rectangle Caret => new(
        new Point(Inset + (_text.Length + CompositionCursor) * CharWidth, Inset),
        new Size(1, Math.Max(0, ActualSize.Height - Inset * 2)));

    /// <summary>Raises the on-screen keyboard on a touch screen, and lets the
    /// form tell the input method where the field is when it takes the focus.</summary>
    public override bool AcceptsTextInput => true;

    bool ITextCompositionTarget.AcceptsComposition => IsEnabled;

    void ITextCompositionTarget.SetComposition(string text, int cursor)
    {
        Composition = text;
        CompositionCursor = cursor;
        InvalidateVisual();
    }

    /// <summary>Open a list under the field, in the field's form. False — the field
    /// isn't in a form.</summary>
    public bool OpenSuggestions(UIElement list)
    {
        if (FindForm() is not { } form) return false;

        form.ShowFlyout(this, list);
        return true;
    }

    protected override void OnTextInput(char c)
    {
        if (char.IsControl(c)) return;

        _text.Append(c);
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
    }

    protected override List<MenuItem>? GetDefaultContextMenu()
    {
        var clear = new MenuItem { Text = "Clear", IsEnabled = _text.Length > 0 };

        clear.Click += (_, _) =>
        {
            _text.Clear();
            InvalidateVisual();
        };

        return [clear];
    }

    protected override void DrawContent(Graphics g)
    {
        if (!IsFocused) return;

        Rectangle caret = Caret;

        g.FillRectangle(caret, Colors.Black);

        // the candidate list of the input method opens under the caret
        ReportTextInputArea(caret);
    }
}