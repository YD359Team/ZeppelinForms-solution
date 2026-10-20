using SkiaSharp;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Headless;

namespace ZeppelinForms.Skia;

/// <summary>A form or an element drawn onto a Skia canvas the caller holds — a
/// snapshot, a benchmark, the designer. The frame itself is
/// <see cref="FormRenderer"/>'s: since 0.18 every backend draws the same one.</summary>
public static class SkiaRenderer
{
    /// <param name="clearBackground">Clear the whole canvas before drawing.
    /// false is needed where several forms share one surface: a dialog has no right
    /// to erase what the owner window drew under it.</param>
    /// <param name="origin">The form's offset on the surface in logical units.
    /// Zero by default — the form takes the whole surface.</param>
    public static void Render(
        Form form,
        SKCanvas canvas,
        float scale = 1f,
        Rectangle? clip = null,
        bool clearBackground = true,
        Point origin = default)
    {
        // back to this count in finally rather than a Restore at the end: a GL
        // surface keeps its canvas between frames, and a frame broken off by an
        // exception left its Save behind — every next frame drew with that frame's
        // scale and clip
        int saveCount = canvas.Save();

        try
        {
            FormRenderer.Render(form, new SkiaGraphics(canvas), scale, clip, clearBackground, origin);
        }
        finally
        {
            canvas.RestoreToCount(saveCount);
        }
    }

    public static void DrawElement(UIElement element, Graphics g) => ElementTreeRenderer.Draw(element, g);
}