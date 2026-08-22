using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Interfaces;

public interface ITextElement
{
    string? Text { get; set; }
    Color TextColor { get; set; }
    HorizontalAlign HorizontalAlign { get; set; }
    VerticalAlign VerticalAlign { get; set; }
}