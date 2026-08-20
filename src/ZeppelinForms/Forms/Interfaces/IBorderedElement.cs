using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Interfaces;

/// <summary>
/// Elements with border
/// </summary>
public interface IBorderedElement
{
    Color BorderColor { get; set; }
    float BorderWidth { get; set; }
}
