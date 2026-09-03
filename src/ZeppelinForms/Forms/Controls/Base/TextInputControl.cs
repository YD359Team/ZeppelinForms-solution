using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Base;

// Base/TextInputControl.cs
/// <summary>Основа полей ввода: мигающая каретка и её жизненный цикл.
/// Логика редактирования — за наследниками.</summary>
public abstract class TextInputControl : InteractiveControl
{
    private const int BlinkIntervalMs = 530;

    private readonly System.Threading.Timer _blinkTimer;
    private bool _disposed;

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

        if (IsFocused && !_disposed)
            _blinkTimer.Change(BlinkIntervalMs, BlinkIntervalMs);
    }

    private void OnBlink(object? state)
    {
        if (_disposed) return;

        FindOwner()?.Invoke(() =>
        {
            CaretVisible = !CaretVisible;
            InvalidateVisual();
        });
    }

    protected override void OnDetached()
    {
        _disposed = true;
        _blinkTimer.Dispose();
    }
}