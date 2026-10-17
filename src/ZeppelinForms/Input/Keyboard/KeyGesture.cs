using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace ZeppelinForms.Input.Keyboard;

/// <summary>A key with the modifiers held for it — Ctrl+S, F5, Ctrl+Shift+Z: a
/// shortcut of a menu item, a command or a key binding of the form.</summary>
/// <remarks>
/// <para>
/// The modifiers must match exactly: Ctrl+S doesn't answer Ctrl+Shift+S. That keeps
/// two shortcuts that differ only by Shift — Undo and Redo as Ctrl+Z and
/// Ctrl+Shift+Z — apart.
/// </para>
/// <para>
/// The key is a physical key, named by its place on a US keyboard, as
/// <see cref="Key"/> is: Ctrl+Z is the same key in every layout. The text is the
/// same one Windows shows in its menus, "Ctrl+Shift+S".
/// </para>
/// </remarks>
public readonly struct KeyGesture : IEquatable<KeyGesture>
{
    public KeyGesture(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        Key = key;
        Modifiers = modifiers;
    }

    public Key Key { get; }

    public KeyModifiers Modifiers { get; }

    /// <summary>Whether a key pressed with these modifiers is this gesture.</summary>
    public bool Matches(Key key, KeyModifiers modifiers) =>
        key != Key.None && key == Key && modifiers == Modifiers;

    /// <summary>The gesture as menus show it: "Ctrl+Shift+S", "F5", "Ctrl+=".</summary>
    public override string ToString()
    {
        var text = new StringBuilder();

        // the order of Windows menus: Ctrl, Alt, Shift
        if (Modifiers.HasFlag(KeyModifiers.Control)) text.Append("Ctrl+");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) text.Append("Alt+");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) text.Append("Shift+");

        text.Append(NameOf(Key));

        return text.ToString();
    }

    /// <summary>Read a gesture as <see cref="ToString"/> writes it: "Ctrl+S",
    /// "Ctrl+Shift+Z", "F5", "Alt+Enter". Case doesn't matter, "Control" is taken
    /// for "Ctrl", and the key can also be named as <see cref="Key"/> names it —
    /// "OemPlus", "NumPad5".</summary>
    /// <exception cref="FormatException">The text names no gesture.</exception>
    public static KeyGesture Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return TryParse(text, out KeyGesture gesture)
            ? gesture
            : throw new FormatException($"\"{text}\" is not a key gesture, such as \"Ctrl+S\" or \"F5\".");
    }

    /// <summary>Read a gesture; false — the text names none.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out KeyGesture gesture)
    {
        gesture = default;

        if (string.IsNullOrWhiteSpace(text)) return false;

        // a key named with a plus of its own — "Ctrl++", "Ctrl+Num +" — splits into an
        // empty last part: the plus goes back onto the part before it
        string[] parts = text.Split('+');
        string keyPart = parts[^1].Trim();
        int modifierCount = parts.Length - 1;

        if (keyPart.Length == 0 && parts.Length >= 2)
        {
            keyPart = parts[^2].Trim() + "+";
            modifierCount = parts.Length - 2;
        }

        var modifiers = KeyModifiers.None;

        for (int i = 0; i < modifierCount; i++)
        {
            switch (parts[i].Trim().ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": modifiers |= KeyModifiers.Control; break;
                case "ALT": modifiers |= KeyModifiers.Alt; break;
                case "SHIFT": modifiers |= KeyModifiers.Shift; break;
                default: return false;
            }
        }

        if (KeyOf(keyPart) is not { } key) return false;

        gesture = new KeyGesture(key, modifiers);
        return true;
    }

    /// <summary>The key's name as menus show it.</summary>
    private static string NameOf(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (char)('0' + (key - Key.NumPad0)),

        Key.Escape => "Esc",
        Key.Delete => "Del",
        Key.Insert => "Ins",
        Key.PageUp => "PgUp",
        Key.PageDown => "PgDn",
        Key.PrintScreen => "PrtSc",

        Key.Add => "Num +",
        Key.Subtract => "Num -",
        Key.Multiply => "Num *",
        Key.Divide => "Num /",
        Key.Decimal => "Num .",

        Key.OemPlus => "=",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemTilde => "`",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        Key.OemQuotes => "'",

        _ => key.ToString(),
    };

    /// <summary>The key a name stands for: as <see cref="NameOf"/> writes it, or as
    /// the enum names it.</summary>
    private static Key? KeyOf(string name)
    {
        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);

            if (c is >= 'A' and <= 'Z') return Key.A + (c - 'A');
            if (c is >= '0' and <= '9') return Key.D0 + (c - '0');

            // "Ctrl++" is how menus write the key that types "+" with Shift and "="
            // without: the same physical key
            if (c == '+') return Key.OemPlus;
        }

        // spaces don't count: "Num+" and "Num +" are the same key
        string bare = name.Replace(" ", string.Empty, StringComparison.Ordinal);

        foreach (Key key in Enum.GetValues<Key>())
        {
            if (key != Key.None && string.Equals(
                NameOf(key).Replace(" ", string.Empty, StringComparison.Ordinal), bare, StringComparison.OrdinalIgnoreCase))
                return key;
        }

        // "Escape", "Enter", "OemPlus", "NumPad5": the enum's own names
        return Enum.TryParse(name, ignoreCase: true, out Key named) && named != Key.None && !int.TryParse(name, out _)
            ? named
            : null;
    }

    public bool Equals(KeyGesture other) => Key == other.Key && Modifiers == other.Modifiers;

    public override bool Equals(object? obj) => obj is KeyGesture other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Key, Modifiers);

    public static bool operator ==(KeyGesture left, KeyGesture right) => left.Equals(right);

    public static bool operator !=(KeyGesture left, KeyGesture right) => !left.Equals(right);
}