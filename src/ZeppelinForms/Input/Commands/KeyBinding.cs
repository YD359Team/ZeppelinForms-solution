using System.Windows.Input;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Input.Commands;

/// <summary>A shortcut of the form that runs a command — for an action with no
/// menu item to carry the shortcut: a toolbar button, a key of a canvas.</summary>
/// <remarks>Added to <c>Form.KeyBindings</c>. The form tries its bindings after the
/// focused element left the key unhandled, so a text field keeps Ctrl+C and Ctrl+Z.</remarks>
public sealed class KeyBinding
{
    public KeyBinding(KeyGesture gesture, ICommand command, object? commandParameter = null)
    {
        ArgumentNullException.ThrowIfNull(command);

        Gesture = gesture;
        Command = command;
        CommandParameter = commandParameter;
    }

    /// <summary>The command's own shortcut, <see cref="Commands.Command.Gesture"/>.</summary>
    /// <exception cref="ArgumentException">The command has no gesture.</exception>
    public KeyBinding(Command command, object? commandParameter = null)
        : this(GestureOf(command), command, commandParameter)
    {
    }

    public KeyGesture Gesture { get; }

    public ICommand Command { get; }

    public object? CommandParameter { get; }

    private static KeyGesture GestureOf(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Gesture
            ?? throw new ArgumentException("The command has no Gesture: give the binding one.", nameof(command));
    }
}