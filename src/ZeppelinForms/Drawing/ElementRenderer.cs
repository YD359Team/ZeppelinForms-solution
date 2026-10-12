using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Drawing;

public static class ElementRenderer
{
    public static IElementRenderer Current { get; set; } = new NotRegisteredElementRenderer();

    private sealed class NotRegisteredElementRenderer : IElementRenderer
    {
        // the class is SkiaOffscreenRenderer: the message used to name a
        // SkiaElementRenderer that never existed
        public Image Render(UIElement element, int width, int height) =>
            throw new ZfSetupException(
                "No element renderer is registered. Call SkiaOffscreenRenderer.Register().");

    }
}