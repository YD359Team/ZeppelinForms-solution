using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Text;

public class LinkLabel : RichLabel
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

    public string? Url { get; set; }

    public Color LinkColor { get; set; } = new Color(255, 0x0D, 0x6E, 0xFD);
    public Color VisitedColor { get; set; } = new Color(255, 0x6F, 0x42, 0xC1);
    public Color HoverColor { get; set; } = new Color(255, 0x0A, 0x58, 0xCA);

    public bool IsVisited { get; private set; }

    public event EventHandler? Navigate;

    public LinkLabel()
    {
        Cursor = CursorKind.Hand;
        ContentAlign = HorizontalContentAlignment.Left;
    }

    private void RebuildInlines()
    {
        Color color = IsHovered ? HoverColor : (IsVisited ? VisitedColor : LinkColor);

        Inlines.Clear();

        if (!string.IsNullOrEmpty(_text))
            Inlines.Add(new TextRun(_text) { Color = color, Underline = true });

        Invalidate();
    }

    protected override void OnMouseOver() => RebuildInlines();
    protected override void OnMouseLeave() => RebuildInlines();

    protected override void OnClick(MouseClickEventArgs e)
    {
        IsVisited = true;
        RebuildInlines();

        Navigate?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}