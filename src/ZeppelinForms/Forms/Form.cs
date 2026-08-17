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
    public Control? Content { get; set; }

    public void Show()
    {
        PlatformWindow?.Show();
    }
}
