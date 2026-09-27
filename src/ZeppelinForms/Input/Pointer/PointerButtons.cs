namespace ZeppelinForms.Input.Pointer;

/// <summary>Buttons held by one contact. A separate type, because
/// <see cref="Mouse.MouseButton"/> is an enumeration of values, not flags:
/// Left equals zero, and a mask can't be built from it.</summary>
[Flags]
public enum PointerButtons
{
    None = 0,
    Left = 1,
    Middle = 2,
    Right = 4,
}

public static class PointerButtonsExtensions
{
    public static PointerButtons ToFlag(this Mouse.MouseButton button) => button switch
    {
        Mouse.MouseButton.Left => PointerButtons.Left,
        Mouse.MouseButton.Middle => PointerButtons.Middle,
        Mouse.MouseButton.Right => PointerButtons.Right,
        _ => PointerButtons.None,
    };
}