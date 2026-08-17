using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls;

public abstract class UIElement
{
    public abstract void Draw(Graphics g);
    public Point Position { get; set; }
    public Size Size { get; set; }
}

public abstract class Control : UIElement
{

}

public abstract class Panel : UIElement
{

}

public class Label : Control
{
    public string? Text { get; set; }

    public override void Draw(Graphics g)
    {
        // e.g. g.DrawString(this.Text);
    }
}
