using System.Windows.Input;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Input.Commands;

/// <summary>A shortcut of the form that runs a command — for an action with no
/// menu item to carry the shortcut: a toolbar button, a key of a canvas.</summary>
/// <remarks>Added to <c>Form.KeyBindings</c>. The form tries its bindings after the
/// focused element left the key unhandled, so a text field keeps Ctrl+C and Ctrl+Z.</remarks>
public sealed class KeyBinding
{
    public KeyBinding(KeyGesture gesture, ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Gesture = gesture;
        Command = command;
    }

    /// <summary>The command's own shortcut, <see cref="Commands.Command.Gesture"/>.</summary>
    /// <exception cref="ArgumentException">The command has no gesture.</exception>
    public KeyBinding(Command command)
        : this(GestureOf(command), command)
    {
    }

    public KeyGesture Gesture { get; }

    public ICommand Command { get; }

    /// <summary>What the command gets as its parameter. An initializer rather than a
    /// constructor argument: both constructors would need it as an optional parameter,
    /// and two overloads with optional parameters can't grow without breaking callers.</summary>
    public object? CommandParameter { get; init; }

    private static KeyGesture GestureOf(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Gesture
            ?? throw new ArgumentException("The command has no Gesture: give the binding one.", nameof(command));
    }
}