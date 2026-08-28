using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Interfaces;

public interface ITextElement
{
    string? Text { get; set; }
    Color TextColor { get; set; }
    HorizontalContentAlignment HorizontalContentAlign { get; set; }
    VerticalContentAlignment VerticalContentAlign { get; set; }
}