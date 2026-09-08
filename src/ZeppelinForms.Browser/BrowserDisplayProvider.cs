using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Browser;

/// <summary>
/// Экран в браузере один и о настоящих мониторах ничего не известно.
/// Рабочая область равна области просмотра, а не экрана: за её пределы
/// нам всё равно не вылезти.
/// </summary>
internal sealed class BrowserDisplayProvider : IDisplayProvider
{
    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var bounds = new Rectangle(
            new Point(0, 0),
            new Size(Interop.ViewportWidth(), Interop.ViewportHeight()));

        return
        [
            new DisplayInfo
            {
                Bounds = bounds,
                WorkingArea = bounds,
                Scale = (float)Interop.DevicePixelRatio(),
                IsPrimary = true,
                Name = "browser",
            },
        ];
    }
}