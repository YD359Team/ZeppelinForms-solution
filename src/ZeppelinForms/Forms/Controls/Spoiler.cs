using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Control can collapse\expand child content
/// </summary>
public class Spoiler : WrapControl, IBorderedElement
{
    public bool IsCollapsed { get; set; }
    // IBorderedElement
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 0f;

    public Spoiler() => Size = new Size(200, 100);

    public override void Draw(Graphics g)
    {
        if (this.BorderWidth > 0)
        {
            g.DrawRectangle(this.LocalBounds, this.BorderColor, this.BorderWidth);
        }
    }

    protected virtual void OnCollapsedStateChanged(bool isCollapsed)
    {
        // called when IsCollapsed changed
    }
}