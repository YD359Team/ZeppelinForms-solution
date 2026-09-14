using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Browser;

/// <summary>
/// Экран в браузере один и о настоящих мониторах ничего не известно.
/// Рабочая область равна области просмотра, а не экрана: за её пределы
/// нам всё равно не вылезти.
/// </summary>
internal sealed class BrowserDisplayProvider : IDisplayProvider
{
    /// <summary>CSS определяет пиксель как 1/96 дюйма, и devicePixelRatio
    /// считается именно от этой базы. Плотность физического экрана браузер
    /// не сообщает вовсе — и это правильное значение, а не заглушка:
    /// в CSS-пикселях миллиметр по определению равен 96/25.4 единицы,
    /// сколько бы точек ни было у настоящей матрицы.</summary>
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