using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Linux;

internal static class X11KeyMap
{
    public static Key ToKey(nuint keysym) => keysym switch
    {
        0xFF09 => Key.Tab,
        0xFF0D => Key.Enter,
        0xFF1B => Key.Escape,
        0x0020 => Key.Space,
        0xFF08 => Key.Back,
        0xFFFF => Key.Delete,
        0xFF50 => Key.Home,
        0xFF57 => Key.End,
        0xFF51 => Key.Left,
        0xFF52 => Key.Up,
        0xFF53 => Key.Right,
        0xFF54 => Key.Down,
        >= 0xFFBE and <= 0xFFC9 => (Key)(Key.F1 + (int)(keysym - 0xFFBE)),
        // латиница: X11 отдаёт ASCII-код, наш Key совпадает с VK для A-Z
        >= 0x0061 and <= 0x007A => (Key)(keysym - 0x0020),
        >= 0x0041 and <= 0x005A => (Key)keysym,
        _ => Key.None,
    };
}
