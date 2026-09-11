using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Base;

// Base/TextInputControl.cs
/// <summary>Основа полей ввода: мигающая каретка и её жизненный цикл.
/// Логика редактирования — за наследниками.</summary>
public abstract class TextInputControl : InteractiveControl
{
    private const int BlinkIntervalMs = 530;

    private readonly System.Threading.Timer _blinkTimer;

    protected bool CaretVisible { get; private set; }

    protected TextInputControl()
    {
        Cursor = CursorKind.IBeam;
        _blinkTimer = new System.Threading.Timer(OnBlink, null, Timeout.Infinite, Timeout.Infinite);
    }

    protected override void OnGotFocus()
    {
        CaretVisible = true;
        _blinkTimer.Change(BlinkIntervalMs, BlinkIntervalMs);
    }

    protected override void OnLostFocus()
    {
        _blinkTimer.Change(Timeout.Infinite, Timeout.Infinite);
        CaretVisible = false;
    }


    protected void ResetCaretBlink()
    {
        CaretVisible = true;

        if (IsFocused)
            _blinkTimer.Change(BlinkIntervalMs, BlinkIntervalMs);
    }

    private void OnBlink(object? state)
    {
        // контрол мог уйти из дерева между срабатыванием таймера и этой
        // строкой — тогда владельца нет, и мигать уже некуда
        FindOwner()?.Invoke(() =>
        {
            CaretVisible = !CaretVisible;
            InvalidateVisual();
        });
    }

    protected override void OnDetached()
    {
        // не Dispose: контрол могут вернуть в дерево — при переключении
        // страницы, пересборке панели, перетаскивании. Остановленный таймер
        // не держится очередью и соберётся сам, если контрол больше не нужен
        _blinkTimer.Change(Timeout.Infinite, Timeout.Infinite);
        CaretVisible = false;
    }
}