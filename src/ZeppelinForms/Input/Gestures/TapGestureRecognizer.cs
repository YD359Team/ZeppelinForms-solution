using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Gestures;

/// <summary>Короткое касание без смещения.</summary>
/// <remarks>
/// Для ведущего контакта это почти дубль обычного Click — тот приходит
/// раньше и работает как работал. Смысл распознавателя в остальном:
/// он видит и неведущие контакты, и участвует в арбитраже, то есть
/// умеет проиграть pan, чего Click не умеет.
/// </remarks>
public sealed class TapGestureRecognizer : GestureRecognizer
{
    public event EventHandler? Tapped;

    protected override void OnPointerMove(PointerEventArgs e)
    {
        if (Contact is not PointerContact contact) return;

        if (contact.TravelDistance > PointerThresholds.TapSlop(contact.Kind, Display))
            Reject();
    }

    protected override void OnPointerUp(PointerEventArgs e)
    {
        if (Contact is not PointerContact contact) return;

        if (contact.Duration > PointerThresholds.TapMaxDurationMs)
        {
            Reject();
            return;
        }

        // принимаем на отпускании: до него отличить касание от начала
        // перетаскивания нельзя, и ранняя победа отобрала бы контакт
        // у pan, который ещё не набрал порога
        Accept();
        Tapped?.Invoke(this, EventArgs.Empty);
    }
}