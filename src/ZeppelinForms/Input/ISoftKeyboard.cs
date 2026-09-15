namespace ZeppelinForms;

/// <summary>Какую раскладку просить у экранной клавиатуры.</summary>
public enum SoftKeyboardKind
{
    Text,
    Number,
    Decimal,
    Email,
    Phone,
    Url,
    Password,
}

/// <summary>Окно, умеющее показывать экранную клавиатуру.</summary>
/// <remarks>
/// Отдельным интерфейсом, а не методами в IPlatformWindow, по той же
/// причине, по которой в 0.9 отделили IDesktopWindow: четыре настольных
/// бэкенда получили бы два метода, которые нечем наполнить, кроме пустого
/// тела, — и пустое тело врёт, потому что неотличимо от «показал».
/// Проверяется приведением: if (PlatformWindow is ISoftKeyboard keyboard).
/// </remarks>
public interface ISoftKeyboard
{
    void ShowSoftKeyboard(SoftKeyboardKind kind);

    void HideSoftKeyboard();
}