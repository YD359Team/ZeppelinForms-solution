namespace ZeppelinForms.Input.Keyboard;

/// <summary>Состояние клавиатуры на текущий момент. Обновляется формой
/// из событий ввода — это не опрос системы, а слепок последнего
/// известного состояния.</summary>
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

    /// <summary>Окно потеряло фокус: о отпусканиях мы больше не узнаем,
    /// поэтому считаем, что не нажато ничего. Иначе Shift «залипнет».</summary>
    internal static void Reset()
    {
        Down.Clear();
        Modifiers = KeyModifiers.None;
    }
}