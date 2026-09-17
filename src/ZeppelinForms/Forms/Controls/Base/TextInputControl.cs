using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Base;

// Base/TextInputControl.cs
/// <summary>Основа полей ввода: мигающая каретка и её жизненный цикл.
/// Логика редактирования — за наследниками.</summary>
/// <remarks>
/// Состояние каретки не хранится, а вычисляется из времени: фаза — это
/// остаток от деления прожитого с последнего сброса на период. Своего
/// таймера у каретки поэтому нет, а будильник общих часов нужен только
/// чтобы перерисовать её в момент переключения.
///
/// Раньше состояние хранилось и переключалось таймером из пула потоков.
/// Отложенный тик, добравшийся до очереди UI после потери фокуса, включал
/// каретку обратно, и она оставалась на нефокусном поле.
/// </remarks>
public abstract class TextInputControl : InteractiveControl
{
    private const double BlinkIntervalMs = 530;

    /// <summary>Время последнего сброса мигания по часам формы.</summary>
    private TimeSpan _blinkStart;

    private IDisposable? _blinkWake;

    protected bool CaretVisible
    {
        get
        {
            if (!IsFocused) return false;

            // часов нет — нет и кадров, в которых мигать: показываем каретку
            // постоянно, иначе она пропадёт насовсем
            if (FindOwner() is not { } owner) return true;

            double elapsed = (owner.Clock.Now - _blinkStart).TotalMilliseconds;

            return elapsed % (BlinkIntervalMs * 2) < BlinkIntervalMs;
        }
    }

    public override bool AcceptsTextInput => IsEnabled;

    protected TextInputControl()
    {
        Cursor = CursorKind.IBeam;
    }

    protected override void OnGotFocus()
    {
        RestartBlink();
    }

    protected override void OnLostFocus()
    {
        StopBlink();
    }

    /// <summary>Каретка обязана быть видна сразу после ввода, перемещения
    /// или выделения — иначе курсор пропадает именно в тот момент,
    /// когда на него смотрят.</summary>
    protected void ResetCaretBlink()
    {
        if (!IsFocused) return;

        RestartBlink();
        InvalidateVisual();
    }

    private void RestartBlink()
    {
        if (FindOwner() is not { } owner) return;

        _blinkStart = owner.Clock.Now;

        ScheduleBlink(owner);
    }

    private void ScheduleBlink(Form owner)
    {
        _blinkWake?.Dispose();

        double elapsed = (owner.Clock.Now - _blinkStart).TotalMilliseconds;
        double untilFlip = BlinkIntervalMs - elapsed % BlinkIntervalMs;

        _blinkWake = owner.Clock.Schedule(TimeSpan.FromMilliseconds(untilFlip), () =>
        {
            // фокус мог уйти, пока будильник стоял в очереди
            if (!IsFocused)
            {
                StopBlink();
                return;
            }

            InvalidateVisual();
            ScheduleBlink(owner);
        });
    }

    private void StopBlink()
    {
        _blinkWake?.Dispose();
        _blinkWake = null;
    }

    protected override void OnDetached()
    {
        // не Dispose: контрол могут вернуть в дерево — при переключении
        // страницы, пересборке панели, перетаскивании. Будильник снимаем,
        // а фаза восстановится сама при следующем получении фокуса
        StopBlink();
    }
}