using System.ComponentModel;

namespace ZeppelinForms.Input.Commands;

/// <summary>A command with a state that is on or off — a grid shown, lighting on —
/// shared by everything that runs it: a menu item shows the check mark, a toggle
/// button stays pressed, and each follows the other.</summary>
/// <remarks>
/// Running it flips <see cref="IsChecked"/>. Setting <see cref="IsChecked"/> from
/// code is the same change: the menu and the buttons follow it, and the callback
/// runs. One flag, so the check in the menu and the button on the toolbar can't
/// disagree.
/// </remarks>
public class ToggleCommand : Command, INotifyPropertyChanged
{
    private readonly Action<bool>? _changed;

    /// <param name="changed">Runs after every change of <see cref="IsChecked"/>,
    /// with the new state — from a menu, a button, a shortcut or code.</param>
    /// <param name="canExecute">Whether it can be switched now; null — always.</param>
    public ToggleCommand(Action<bool>? changed = null, Func<bool>? canExecute = null)
        : base(static () => { }, canExecute)
    {
        _changed = changed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsChecked
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            _changed?.Invoke(value);
        }
    }

    /// <summary>Flip <see cref="IsChecked"/>, if it can be switched now.</summary>
    public override void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        IsChecked = !IsChecked;
    }
}