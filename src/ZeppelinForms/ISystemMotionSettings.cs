namespace ZeppelinForms;

/// <summary>
/// Системная настройка «уменьшить движение»: пользователь попросил
/// интерфейс меньше анимировать — из-за вестибулярных расстройств,
/// мигрени или просто потому, что так ему удобнее.
/// </summary>
/// <remarks>
/// Платформа реализует это по возможности, как и IClipboard или
/// IAppLifecycle: у Windows это «Показывать анимацию в Windows»,
/// у Android — масштаб длительности анимаций, равный нулю, у браузера —
/// медиазапрос prefers-reduced-motion. Где настройки нет, приложение
/// решает само через Motion.Preference.
/// </remarks>
public interface ISystemMotionSettings
{
    bool PrefersReducedMotion { get; }

    /// <summary>Пользователь поменял настройку, пока приложение работало.</summary>
    event EventHandler? Changed;
}