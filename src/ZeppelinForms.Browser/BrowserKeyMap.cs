using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Browser;

/// <summary>
/// KeyboardEvent.code — физическая клавиша, независимая от раскладки,
/// поэтому соответствие с Key однозначное и таблицей, а не вычислением.
/// </summary>
internal static class BrowserKeyMap
{
    public static Key FromCode(string code)
    {
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal))
            return (Key)(Key.A + (code[3] - 'A'));

        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal))
            return (Key)(Key.D0 + (code[5] - '0'));

        if (code.Length == 7 && code.StartsWith("Numpad", StringComparison.Ordinal) && char.IsAsciiDigit(code[6]))
            return (Key)(Key.NumPad0 + (code[6] - '0'));

        return code switch
        {
            "Backspace" => Key.Back,
            "Tab" => Key.Tab,
            "Enter" or "NumpadEnter" => Key.Enter,
            "Pause" => Key.Pause,
            "CapsLock" => Key.CapsLock,
            "Escape" => Key.Escape,
            "Space" => Key.Space,

            "PageUp" => Key.PageUp,
            "PageDown" => Key.PageDown,
            "End" => Key.End,
            "Home" => Key.Home,
            "ArrowLeft" => Key.Left,
            "ArrowUp" => Key.Up,
            "ArrowRight" => Key.Right,
            "ArrowDown" => Key.Down,
            "PrintScreen" => Key.PrintScreen,
            "Insert" => Key.Insert,
            "Delete" => Key.Delete,

            "MetaLeft" => Key.LeftWindows,
            "MetaRight" => Key.RightWindows,
            "ContextMenu" => Key.Apps,

            "NumpadMultiply" => Key.Multiply,
            "NumpadAdd" => Key.Add,
            "NumpadSubtract" => Key.Subtract,
            "NumpadDecimal" => Key.Decimal,
            "NumpadDivide" => Key.Divide,

            "NumLock" => Key.NumLock,
            "ScrollLock" => Key.ScrollLock,

            "ShiftLeft" => Key.LeftShift,
            "ShiftRight" => Key.RightShift,
            "ControlLeft" => Key.LeftControl,
            "ControlRight" => Key.RightControl,
            "AltLeft" => Key.LeftAlt,
            "AltRight" => Key.RightAlt,

            "Semicolon" => Key.OemSemicolon,
            "Equal" => Key.OemPlus,
            "Comma" => Key.OemComma,
            "Minus" => Key.OemMinus,
            "Period" => Key.OemPeriod,
            "Slash" => Key.OemQuestion,
            "Backquote" => Key.OemTilde,
            "BracketLeft" => Key.OemOpenBrackets,
            "Backslash" => Key.OemPipe,
            "BracketRight" => Key.OemCloseBrackets,
            "Quote" => Key.OemQuotes,
            "IntlBackslash" => Key.OemBackslash,

            _ => FunctionKey(code),
        };
    }

    private static Key FunctionKey(string code)
    {
        if (code.Length < 2 || code[0] != 'F' || !char.IsAsciiDigit(code[1]))
            return Key.None;

        return int.TryParse(code.AsSpan(1), out int number) && number is >= 1 and <= 24
            ? (Key)(Key.F1 + (number - 1))
            : Key.None;
    }

    /// <summary>KeyboardEvent.key длиной в один печатный символ — это ввод
    /// текста. Всё остальное ("ArrowUp", "Shift", "Dead") текстом не является.</summary>
    public static char? ToTextInput(string key)
    {
        if (key.Length != 1) return null;

        char c = key[0];
        return char.IsControl(c) ? null : c;
    }
}