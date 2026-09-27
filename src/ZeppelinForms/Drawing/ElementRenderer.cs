using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Drawing;

public static class ElementRenderer
{
    public static IElementRenderer Current { get; set; } = new NotRegisteredElementRenderer();

    private sealed class NotRegisteredElementRenderer : IElementRenderer
    {
        public Image Render(UIElement element, int width, int height) =>
            throw new InvalidOperationException(
                "No element renderer is registered. Call SkiaElementRenderer.Register().");

    }
}