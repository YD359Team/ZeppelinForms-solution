namespace ZeppelinForms.Input.Pointer;

/// <summary>Почему взаимодействие оборвали.</summary>
public enum PointerCancelReason
{
    /// <summary>Системный захват отобрали: чужое окно, Alt+Tab, блокировка
    /// экрана. До 0.11 здесь рассылался поддельный MouseUp.</summary>
    CaptureLost,

    /// <summary>Элемент, который держал контакт, ушёл из дерева.</summary>
    Detached,

    /// <summary>Контакт выиграл другой участник — распознаватель жеста
    /// на предке. Задел под арбитраж из пункта 4; пока не используется.</summary>
    GestureWon,

    /// <summary>Отмену прислала платформа: pointercancel в браузере,
    /// ACTION_CANCEL на Android.</summary>
    Platform,
}