using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Text;

public partial class LinkLabel : RichLabel
{
    public string? Text
    {
        get => _text;
        set
        {
            _text = value;
            RebuildInlines();
        }
    }

    private string? _text;

    /// <summary>The text as shown: the text, or — never set — the name given to the
    /// link, see <see cref="ZeppelinForms.Forms.Controls.Base.UIElement.NameCaption"/>.</summary>
    internal override string? ShownText => _text ?? NameCaption;

    protected override void OnNameCaptionChanged()
    {
        if (_text is null) RebuildInlines();
    }

    public string? Url { get; set; }

    public Color LinkColor { get; set; } = new Color(255, 0x0D, 0x6E, 0xFD);
    public Color VisitedColor { get; set; } = new Color(255, 0x6F, 0x42, 0xC1);

    [Styled(Category = "Link")]
    public partial Color HoverColor { get; set; }
    private static Color HoverColorDefault => new(255, 0x0A, 0x58, 0xCA);

    public bool IsVisited { get; private set; }

    public event EventHandler? Navigate;

    public LinkLabel()
    {
        // the alignment is left to RichLabel's default, Left: set here it would be
        // a local value, which no style sheet could move
        Cursor = CursorKind.Hand;
    }

    private void RebuildInlines()
    {
        Color color = IsHovered ? HoverColor : (IsVisited ? VisitedColor : LinkColor);

        Inlines.Clear();

        if (ShownText is { Length: > 0 } text)
            Inlines.Add(new TextRun(text) { Color = color, Underline = true });

        Invalidate();
    }

    protected override void OnMouseEnter(MouseMoveEventArgs e) => RebuildInlines();
    protected override void OnMouseExit(MouseMoveEventArgs e) => RebuildInlines();

    protected override void OnClick(MouseClickEventArgs e)
    {
        IsVisited = true;
        RebuildInlines();

        Navigate?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}