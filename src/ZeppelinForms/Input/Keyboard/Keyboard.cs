namespace ZeppelinForms.Input.Keyboard;

/// <summary>The keyboard state at the current moment. Updated by the form
/// from input events — this is not polling the system but a snapshot
/// of the last known state.</summary>
public static class Keyboard
{
    private static readonly HashSet<Key> Down = [];

    public static KeyModifiers Modifiers { get; private set; }

    public static bool IsKeyDown(Key key) => Down.Contains(key);

    public static bool IsControlDown => Modifiers.HasFlag(KeyModifiers.Control);
    public static bool IsShiftDown => Modifiers.HasFlag(KeyModifiers.Shift);
    public static bool IsAltDown => Modifiers.HasFlag(KeyModifiers.Alt);

    internal static void OnDown(Key key, KeyModifiers modifiers)
    {
        Down.Add(key);
        Modifiers = modifiers;
    }

    internal static void OnUp(Key key, KeyModifiers modifiers)
    {
        Down.Remove(key);
        Modifiers = modifiers;
    }

    /// <summary>The window lost focus: we won't learn about releases anymore,
    /// so we assume nothing is pressed. Otherwise Shift would "stick".</summary>
    internal static void Reset()
    {
        Down.Clear();
        Modifiers = KeyModifiers.None;
    }
}