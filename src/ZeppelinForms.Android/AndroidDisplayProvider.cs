using Android.Content;
using Android.Util;
using ZeppelinForms.Drawing.Primitives;
using Size = ZeppelinForms.Drawing.Primitives.Size;

namespace ZeppelinForms.Android;

/// <summary>There is one screen. The working area equals the full one: cutouts
/// and system bars are insets inside the window, not another screen.</summary>
internal sealed class AndroidDisplayProvider(Context context) : IDisplayProvider
{
    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        DisplayMetrics metrics = context.Resources?.DisplayMetrics
            ?? throw new InvalidOperationException("DisplayMetrics are unavailable.");

        var bounds = new Rectangle(
            Point.Empty,
            new Size(metrics.WidthPixels, metrics.HeightPixels));

        return
        [
            new DisplayInfo
            {
                Bounds = bounds,
                WorkingArea = bounds,

                // Density is counted from a base of 160 rather than 96 — exactly the
                // case Dpi is separated from Scale for: one can't be restored from
                // the other, the bases differ
                Scale = metrics.Density,
                Dpi = ResolveDpi(metrics),

                IsPrimary = true,
                Name = "android",
            },
        ];
    }

    /// <summary>Xdpi is the real panel density, and on a typical phone it differs
    /// noticeably from DensityDpi. But some manufacturers fill it with garbage,
    /// so its plausibility is checked, and otherwise the rounded system one is taken.</summary>
    private static float ResolveDpi(DisplayMetrics metrics)
    {
        float xdpi = metrics.Xdpi;

        bool plausible = xdpi is > 60f and < 1200f;

        return plausible ? xdpi : (float)metrics.DensityDpi;
    }
}