using Android.Content;
using Android.Util;
using ZeppelinForms.Drawing.Primitives;
using Size = ZeppelinForms.Drawing.Primitives.Size;

namespace ZeppelinForms.Android;

/// <summary>Экран один. Рабочая область равна полной: вырезы и системные
/// панели — это отступы внутри окна, а не другой экран.</summary>
internal sealed class AndroidDisplayProvider(Context context) : IDisplayProvider
{
    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        DisplayMetrics metrics = context.Resources?.DisplayMetrics
            ?? throw new InvalidOperationException("DisplayMetrics недоступны.");

        var bounds = new Rectangle(
            Point.Empty,
            new Size(metrics.WidthPixels, metrics.HeightPixels));

        return
        [
            new DisplayInfo
            {
                Bounds = bounds,
                WorkingArea = bounds,

                // Density считается от базы 160, а не 96 — ровно тот случай,
                // ради которого Dpi отделён от Scale: восстановить одно
                // из другого нельзя, база разная
                Scale = metrics.Density,
                Dpi = ResolveDpi(metrics),

                IsPrimary = true,
                Name = "android",
            },
        ];
    }

    /// <summary>Xdpi — настоящая плотность матрицы, и на типичном телефоне
    /// она заметно расходится с DensityDpi. Но часть производителей
    /// проставляет её мусором, поэтому правдоподобность проверяется,
    /// а иначе берётся округлённая системная.</summary>
    private static float ResolveDpi(DisplayMetrics metrics)
    {
        float xdpi = metrics.Xdpi;

        bool plausible = xdpi is > 60f and < 1200f;

        return plausible ? xdpi : (float)metrics.DensityDpi;
    }
}