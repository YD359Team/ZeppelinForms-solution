namespace ZeppelinForms.Animation;

public enum MotionPreference
{
    /// <summary>Как решила система. Где системной настройки нет —
    /// анимация включена.</summary>
    System,

    /// <summary>Анимировать всегда, что бы ни говорила система.</summary>
    Full,

    /// <summary>Не анимировать, что бы ни говорила система.</summary>
    Reduced,
}

/// <summary>
/// Уменьшать ли движение: общий ответ для всего фреймворка.
/// </summary>
/// <remarks>
/// Уменьшенное движение — это не «выключить всё»: переходы свойств,
/// переезды раскладки, появление и исчезание, смена страниц и волна
/// темы становятся мгновенными, потому что это движение ради красоты.
/// А инерция прокрутки и индикаторы загрузки остаются: первое —
/// прямое следствие жеста пользователя, второе сообщает, что работа
/// идёт, и без движения этот смысл пропадёт.
/// </remarks>
public static class Motion
{
    private static ISystemMotionSettings? s_system;

    /// <summary>Решение приложения поверх системного.</summary>
    public static MotionPreference Preference
    {
        get;
        set
        {
            if (field == value) return;

            bool before = IsReduced;
            field = value;

            if (before != IsReduced) Changed?.Invoke(null, EventArgs.Empty);
        }
    } = MotionPreference.System;

    /// <summary>Уменьшать ли движение прямо сейчас.</summary>
    public static bool IsReduced => Preference switch
    {
        MotionPreference.Reduced => true,
        MotionPreference.Full => false,
        _ => s_system?.PrefersReducedMotion ?? false,
    };

    /// <summary>Ответ поменялся — из-за приложения или из-за системы.</summary>
    public static event EventHandler? Changed;

    /// <summary>Подключить системную настройку. Зовёт платформа при запуске.</summary>
    public static void UseSystemSettings(ISystemMotionSettings settings)
    {
        if (s_system is not null) s_system.Changed -= OnSystemChanged;

        s_system = settings;
        s_system.Changed += OnSystemChanged;
    }

    private static void OnSystemChanged(object? sender, EventArgs e)
    {
        // решение приложения перекрывает систему — тогда смена в системе
        // ничего не меняет, и сообщать не о чем
        if (Preference == MotionPreference.System)
            Changed?.Invoke(null, EventArgs.Empty);
    }
}