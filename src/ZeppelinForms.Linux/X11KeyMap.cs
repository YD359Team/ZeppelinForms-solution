using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Linux;

internal static class X11KeyMap
{
    public static Key ToKey(nuint keysym) => keysym switch
    {
        // управление и навигация
        0xFF08 => Key.Back,
        0xFF09 => Key.Tab,
        0xFF0B => Key.Clear,
        0xFF0D => Key.Enter,
        0xFF13 => Key.Pause,
        0xFF14 => Key.ScrollLock,
        0xFF1B => Key.Escape,
        0xFF50 => Key.Home,
        0xFF51 => Key.Left,
        0xFF52 => Key.Up,
        0xFF53 => Key.Right,
        0xFF54 => Key.Down,
        0xFF55 => Key.PageUp,
        0xFF56 => Key.PageDown,
        0xFF57 => Key.End,
        0xFF61 => Key.PrintScreen,
        0xFF63 => Key.Insert,
        0xFF67 => Key.Apps,
        0xFF7F => Key.NumLock,
        0xFFFF => Key.Delete,

        // цифровой блок
        0xFF8D => Key.Enter,      // KP_Enter: отдельного VK нет
        0xFFAA => Key.Multiply,
        0xFFAB => Key.Add,
        0xFFAC => Key.Separator,
        0xFFAD => Key.Subtract,
        0xFFAE => Key.Decimal,
        0xFFAF => Key.Divide,
        >= 0xFFB0 and <= 0xFFB9 => (Key)(Key.NumPad0 + (int)(keysym - 0xFFB0)),

        // F1–F24 идут подряд и там, и там
        >= 0xFFBE and <= 0xFFD5 => (Key)(Key.F1 + (int)(keysym - 0xFFBE)),

        // модификаторы
        0xFFE1 => Key.LeftShift,
        0xFFE2 => Key.RightShift,
        0xFFE3 => Key.LeftControl,
        0xFFE4 => Key.RightControl,
        0xFFE5 => Key.CapsLock,
        0xFFE9 => Key.LeftAlt,
        0xFFEA => Key.RightAlt,
        0xFFEB => Key.LeftWindows,
        0xFFEC => Key.RightWindows,

        // знаки: соответствие ASCII → VK не линейное, только таблицей.
        // Проверяются до диапазонов ниже, иначе попадут в них
        0x0027 => Key.OemQuotes,
        0x002C => Key.OemComma,
        0x002D => Key.OemMinus,
        0x002E => Key.OemPeriod,
        0x002F => Key.OemQuestion,
        0x003B => Key.OemSemicolon,
        0x003D => Key.OemPlus,
        0x005B => Key.OemOpenBrackets,
        0x005C => Key.OemPipe,
        0x005D => Key.OemCloseBrackets,
        0x0060 => Key.OemTilde,

        0x0020 => Key.Space,

        // цифры и латиница: X11 отдаёт ASCII, наш Key совпадает с VK
        >= 0x0030 and <= 0x0039 => (Key)keysym,
        >= 0x0041 and <= 0x005A => (Key)keysym,
        >= 0x0061 and <= 0x007A => (Key)(keysym - 0x0020),

        _ => Key.None,
    };
}
