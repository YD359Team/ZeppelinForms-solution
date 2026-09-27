using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Headless;

/// <summary>No drawing — returns an empty image of the required size.</summary>
public sealed class HeadlessElementRenderer : IElementRenderer
{
    public static void Register() => ElementRenderer.Current = new HeadlessElementRenderer();

    public Image Render(UIElement element, int width, int height)
    {
        int w = Math.Max(1, width);
        int h = Math.Max(1, height);

        // there are no pixels at the output and there can't be, but all the drawing
        // code still honestly runs — that is what headless is for
        ElementTreeRenderer.Draw(element, new HeadlessGraphics());

        return new Image(w, h, new byte[w * h * 4]);
    }
}