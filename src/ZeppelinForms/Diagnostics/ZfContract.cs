using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ZeppelinForms.Diagnostics;

/// <summary>
/// Нарушен внутренний контракт фреймворка: не ошибка пользователя,
/// а признак того, что код контрола или платформы делает что-то,
/// на что остальной код не рассчитан.
/// </summary>
public sealed class ZfContractException(string message) : InvalidOperationException(message);

/// <summary>
/// Проверки внутренних контрактов. Правила вроде «кисть из пула нельзя
/// держать через чужой вызов отрисовки» до сих пор жили в комментариях,
/// то есть соблюдались ровно до первого нового контрола.
/// </summary>
/// <remarks>
/// Все методы помечены [Conditional("DEBUG")]: в релизной сборке
/// вызовы вырезаются компилятором вместе с вычислением аргументов,
/// поэтому проверку можно ставить и на горячем пути.
///
/// Именно поэтому в них нельзя выносить работу, нужную самому коду:
/// в релизе её просто не будет.
/// </remarks>
public static class ZfContract
{
    /// <summary>Что делать при нарушении. По умолчанию — исключение:
    /// контракт нарушается кодом фреймворка, и продолжать после этого
    /// значит отлаживать последствия вместо причины.</summary>
    public static ContractViolationBehavior Behavior { get; set; }
        = ContractViolationBehavior.Throw;

    /// <summary>Нарушение контракта. Подписка нужна тестам: проверить,
    /// что нарушение случилось, не роняя прогон.</summary>
    public static event EventHandler<string>? Violated;

    [Conditional("DEBUG")]
    public static void Require(
        bool condition,
        string message,
        [CallerMemberName] string? member = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        if (condition) return;

        Report($"{message}\n  в {member}, {System.IO.Path.GetFileName(file)}:{line}");
    }

    /// <summary>Безусловное нарушение: код дошёл туда, куда не должен был.</summary>
    [Conditional("DEBUG")]
    public static void Fail(
        string message,
        [CallerMemberName] string? member = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        Report($"{message}\n  в {member}, {System.IO.Path.GetFileName(file)}:{line}");
    }

    private static void Report(string message)
    {
        Violated?.Invoke(null, message);

        switch (Behavior)
        {
            case ContractViolationBehavior.Throw:
                throw new ZfContractException(message);

            case ContractViolationBehavior.Log:
                Debug.WriteLine($"[ZF] нарушен контракт: {message}");
                break;

            case ContractViolationBehavior.Silent:
                break;
        }
    }
}

public enum ContractViolationBehavior
{
    /// <summary>Бросить <see cref="ZfContractException"/>.</summary>
    Throw,

    /// <summary>Написать в отладочный вывод и продолжить. Нужно там,
    /// где нарушение уже известно и чинится отдельно, а прогон ронять
    /// нежелательно.</summary>
    Log,

    /// <summary>Только событие, без вывода. Для тестов, которые нарушение
    /// провоцируют намеренно.</summary>
    Silent,
}