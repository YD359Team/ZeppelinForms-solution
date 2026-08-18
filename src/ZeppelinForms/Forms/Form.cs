using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Forms;

public class Form
{
    internal IPlatformWindow? PlatformWindow { get; set; }

    public string? Title { get; set; }
    public Icon? Icon { get; set; }
    public Point Position { get; set; }
    public Size Size { get; set; }

    public UIElement? Content
    {
        get;
        set
        {
            if (field is not null) field.Owner = null;
            field = value;
            if (value is not null) value.Owner = this;
        }
    }

    public void Show()
    {
        PlatformWindow?.Show();
    }

    internal void Invalidate()
    {
        throw new NotImplementedException();
    }
}
