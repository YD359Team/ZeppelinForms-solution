using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Headless;

/// <summary>Отрисовки нет — отдаёт пустое изображение нужного размера.</summary>
public sealed class HeadlessElementRenderer : IElementRenderer
{
    public static void Register() => ElementRenderer.Current = new HeadlessElementRenderer();

    public Image Render(UIElement element, int width, int height)
    {
        int w = Math.Max(1, width);
        int h = Math.Max(1, height);

        // пикселей на выходе нет и быть не может, но весь код отрисовки
        // при этом честно выполняется — ради этого headless и нужен
        ElementTreeRenderer.Draw(element, new HeadlessGraphics());

        return new Image(w, h, new byte[w * h * 4]);
    }
}
