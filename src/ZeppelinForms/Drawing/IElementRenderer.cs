using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Drawing;

public interface IElementRenderer
{
    Image Render(UIElement element, int width, int height);
}
