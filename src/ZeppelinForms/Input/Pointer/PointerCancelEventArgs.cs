using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Input.Pointer;

/// <summary>Взаимодействие оборвано, завершения не будет.</summary>
/// <remarks>
/// Здесь намеренно нет Handled. Отмена — не предложение: к моменту её
/// доставки контакта уже нет, и отказаться от неё значит остаться
/// нажатым навсегда.
/// </remarks>
public sealed record class PointerCancelEventArgs(
    int PointerId,
    PointerKind Kind,
    Point Location,
    PointerCancelReason Reason) : ZfEventArgs;