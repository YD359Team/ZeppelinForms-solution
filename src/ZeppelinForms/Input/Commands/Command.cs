using System.Windows.Input;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Input.Commands;

/// <summary>An action a menu item, a button and a shortcut share: it runs the same
/// way from each of them, and is available in all of them at once.</summary>
/// <remarks>
/// <para>
/// The standard <see cref="ICommand"/>: a command of an MVVM library — the
/// <c>RelayCommand</c> of CommunityToolkit.Mvvm — works in its place. This one adds
/// the shortcut, <see cref="Gesture"/>, which a menu item shows beside its caption
/// and a form runs from the keyboard.
/// </para>
/// <para>
/// Whether it can run is asked, not stored: a menu asks as it opens, a shortcut
/// before it runs. A button can't ask on every frame, so it listens to
/// <see cref="CanExecuteChanged"/>: call <see cref="NotifyCanExecuteChanged"/>
/// when what <c>canExecute</c> reads changes, on the UI thread.
/// </para>
/// </remarks>
public class Command : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    // Overloads rather than an optional canExecute: two public overloads with
    // optional parameters can't both grow later without breaking callers (RS0026)

    /// <param name="execute">The action; it can always run.</param>
    public Command(Action execute)
        : this(execute, null)
    {
    }

    /// <param name="execute">The action.</param>
    /// <param name="canExecute">Whether it can run now; null — always.</param>
    public Command(Action execute, Func<bool>? canExecute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = _ => execute();
        _canExecute = canExecute is null ? null : _ => canExecute();
    }

    /// <param name="execute">The action, with the parameter of the button or the menu
    /// item that runs it; it can always run.</param>
    public Command(Action<object?> execute)
        : this(execute, null)
    {
    }

    /// <param name="execute">The action, with the parameter of the button or
    /// the menu item that runs it.</param>
    /// <param name="canExecute">Whether it can run now with that parameter; null — always.</param>
    public Command(Action<object?> execute, Func<object?, bool>? canExecute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>The shortcut: shown beside the caption of a menu item with this
    /// command, and run by the form when the item is in its menu bar or the command
    /// is in its <c>KeyBindings</c>.</summary>
    public KeyGesture? Gesture { get; init; }

    public event EventHandler? CanExecuteChanged;

    public virtual bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <summary>Run the action, if it can run now: a shortcut pressed between
    /// a change and the notification about it must not run a disabled action.</summary>
    public virtual void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        _execute(parameter);
    }

    /// <summary>Tell the buttons with this command to ask <see cref="CanExecute"/>
    /// again. Call it on the UI thread.</summary>
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}