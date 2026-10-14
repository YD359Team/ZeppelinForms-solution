using System.Windows.Input;
using ZeppelinForms.Input.Commands;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms.Controls;

public sealed class MenuItem
{
    public string Text { get; set; } = string.Empty;
    public string? PathData { get; set; }
    public bool IsSeparator { get; set; }
    public bool IsEnabled { get; set; } = true;
    public List<MenuItem> Items { get; } = [];

    public event EventHandler? Click;

    /// <summary>The item is about to show its <see cref="Items"/>: the place to set
    /// their <see cref="IsEnabled"/> and <see cref="IsChecked"/> from the state of the
    /// application, once, instead of keeping every item up to date as it changes.</summary>
    /// <remarks>Raised by a menu bar before it opens the item's submenu, and by the
    /// form before it runs the shortcut of one of the items under it — so a shortcut
    /// pressed with the menu closed sees the same state the open menu would show.</remarks>
    public event EventHandler? Opening;

    /// <summary>A click flips <see cref="IsChecked"/>, as a check box does.</summary>
    public bool IsCheckable { get; set; }

    /// <summary>A check mark before the caption. With a <see cref="ToggleCommand"/>
    /// the command's state is shown instead.</summary>
    public bool IsChecked { get; set; }

    /// <summary>The shortcut: shown right of the caption, and run by the form while
    /// the item is in its menu bar. Null — the gesture of the <see cref="Command"/>,
    /// if it is a <see cref="Input.Commands.Command"/> with one.</summary>
    public KeyGesture? KeyGesture { get; set; }

    /// <summary>The shortcut text shown right of the caption when it isn't the
    /// gesture's own — "Ctrl+Wheel", a key the item doesn't run itself. Display only.</summary>
    public string? InputGestureText { get; set; }

    /// <summary>Runs after <see cref="Click"/>. While it can't run, the item is
    /// disabled; a <see cref="ToggleCommand"/> also gives the check mark.</summary>
    public ICommand? Command { get; set; }

    public object? CommandParameter { get; set; }

    public static MenuItem Separator => new() { IsSeparator = true };

    // ===== for the menus =====

    /// <summary>Enabled itself, and its command can run.</summary>
    internal bool IsEffectivelyEnabled =>
        IsEnabled && !IsSeparator && (Command?.CanExecute(CommandParameter) ?? true);

    /// <summary>The check mark shown: the toggle command's state, or the item's own.</summary>
    internal bool IsEffectivelyChecked => Command is ToggleCommand toggle ? toggle.IsChecked : IsChecked;

    /// <summary>The shortcut the form runs.</summary>
    internal KeyGesture? EffectiveGesture => KeyGesture ?? (Command as Command)?.Gesture;

    /// <summary>What is shown right of the caption; null — nothing.</summary>
    internal string? GestureText => InputGestureText ?? EffectiveGesture?.ToString();

    internal void RaiseClick() => Click?.Invoke(this, EventArgs.Empty);

    internal void RaiseOpening() => Opening?.Invoke(this, EventArgs.Empty);

    /// <summary>Run the item as a click on it does: flip the check, raise
    /// <see cref="Click"/>, run the command. False — it is disabled.</summary>
    internal bool Invoke()
    {
        if (!IsEffectivelyEnabled) return false;

        // a toggle command keeps the state itself and flips it as it runs
        if (IsCheckable && Command is not ToggleCommand)
            IsChecked = !IsChecked;

        RaiseClick();
        Command?.Execute(CommandParameter);

        return true;
    }
}