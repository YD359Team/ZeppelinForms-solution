using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Headless;

namespace ZeppelinForms.Gles;

/// <summary>
/// An element drawn into a picture by the GLES backend — <see cref="UIElement.RenderToImage"/>,
/// the color picker's eyedropper: in the GL context of a window, into a framebuffer of its
/// own, read back.
/// </summary>
/// <remarks>Called on the windows' thread. Before any window draws with GL there is no
/// context to draw in: the drawing code still runs, as the headless renderer runs it, and
/// the picture is empty.</remarks>
internal sealed class GlesElementRenderer : IElementRenderer
{
    public static void Register() => ElementRenderer.Current = new GlesElementRenderer();

    public Image Render(UIElement element, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(element);

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (GlesSurface.ForOffscreen() is not { } surface)
        {
            ElementTreeRenderer.Draw(element, new HeadlessGraphics());
            return new Image(width, height, new byte[width * height * 4]);
        }

        return surface.RenderOffscreen(width, height, g =>
        {
            // the walk moves the canvas by the element's position: a picture of it has
            // the element at its origin
            g.Save();
            g.Translate(-element.Position.X, -element.Position.Y);
            ElementTreeRenderer.Draw(element, g);
            g.Restore();
        });
    }
}