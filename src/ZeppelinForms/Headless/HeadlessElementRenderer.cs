using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Headless;

/// <summary>Отрисовки нет — отдаёт пустое изображение нужного размера.</summary>
public sealed class HeadlessElementRenderer : IElementRenderer
{
    public static void Register() => ElementRenderer.Current = new HeadlessElementRenderer();

    public Image Render(UIElement element, int width, int height) =>
        new(Math.Max(1, width), Math.Max(1, height), new byte[Math.Max(1, width) * Math.Max(1, height) * 4]);
}
