using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Browser;

/// <summary>
/// There is one screen in a browser, and nothing is known about real monitors.
/// The working area equals the viewport rather than the screen: we can't get
/// beyond it anyway.
/// </summary>
internal sealed class BrowserDisplayProvider : IDisplayProvider
{
    /// <summary>CSS defines a pixel as 1/96 of an inch, and devicePixelRatio is
    /// counted exactly from that base. The browser doesn't report the physical
    /// screen's density at all — and this is the right value, not a stand-in:
    /// in CSS pixels a millimeter is by definition 96/25.4 units, however many
    /// dots the real panel has.</summary>
    private const float CssDpi = 96f;

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var bounds = new Rectangle(
            new Point(0, 0),
            new Size(Interop.ViewportWidth(), Interop.ViewportHeight()));

        float scale = (float)Interop.DevicePixelRatio();

        return
        [
            new DisplayInfo
            {
                Bounds = bounds,
                WorkingArea = bounds,
                Scale = scale,
                Dpi = CssDpi * scale,
                IsPrimary = true,
                Name = "browser",
            },
        ];
    }
}