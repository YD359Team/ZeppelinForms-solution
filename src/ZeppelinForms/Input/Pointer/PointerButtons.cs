namespace ZeppelinForms.Input.Pointer;

/// <summary>Кнопки, удерживаемые одним контактом. Отдельный тип, потому что
/// <see cref="Mouse.MouseButton"/> — перечисление значений, а не флагов:
/// Left равен нулю, и маску из него не собрать.</summary>
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