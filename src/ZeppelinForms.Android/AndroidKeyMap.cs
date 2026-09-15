using Android.Views;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Android;

/// <summary>Коды Android в клавиши фреймворка.</summary>
/// <remarks>
/// Таблица нужна потому, что значения Key совпадают с виртуальными кодами
/// Win32, а Android нумерует клавиши по-своему. Прямого приведения,
/// как на Windows, здесь не выйдет.
///
/// Раскладка на отображение не влияет: Keycode обозначает физическую
/// клавишу, а введённый символ приходит отдельно, из UnicodeChar.
/// </remarks>
internal static class AndroidKeyMap
{
    public static Key ToKey(Keycode code) => code switch
    {
        >= Keycode.A and <= Keycode.Z => Key.A + (code - Keycode.A),
        >= Keycode.Num0 and <= Keycode.Num9 => Key.D0 + (code - Keycode.Num0),
        >= Keycode.Numpad0 and <= Keycode.Numpad9 => Key.NumPad0 + (code - Keycode.Numpad0),
        >= Keycode.F1 and <= Keycode.F12 => Key.F1 + (code - Keycode.F1),

        Keycode.Del => Key.Backspace,
        Keycode.ForwardDel => Key.Delete,
        Keycode.Enter or Keycode.NumpadEnter => Key.Enter,
        Keycode.Escape => Key.Escape,
        Keycode.Tab => Key.Tab,
        Keycode.Space => Key.Space,

        Keycode.DpadLeft => Key.Left,
        Keycode.DpadRight => Key.Right,
        Keycode.DpadUp => Key.Up,
        Keycode.DpadDown => Key.Down,
        Keycode.MoveHome => Key.Home,
        Keycode.MoveEnd => Key.End,
        Keycode.PageUp => Key.PageUp,
        Keycode.PageDown => Key.PageDown,
        Keycode.Insert => Key.Insert,

        Keycode.ShiftLeft => Key.LeftShift,
        Keycode.ShiftRight => Key.RightShift,
        Keycode.CtrlLeft => Key.LeftControl,
        Keycode.CtrlRight => Key.RightControl,
        Keycode.AltLeft => Key.LeftAlt,
        Keycode.AltRight => Key.RightAlt,

        Keycode.CapsLock => Key.CapsLock,
        Keycode.NumLock => Key.NumLock,
        Keycode.ScrollLock => Key.ScrollLock,
        Keycode.Break => Key.Pause,
        Keycode.Sysrq => Key.PrintScreen,

        // знаки названы по положению на американской клавиатуре —
        // так же, как в Key, который повторяет соглашение Win32
        Keycode.Semicolon => Key.OemSemicolon,
        Keycode.Equals => Key.OemPlus,
        Keycode.Comma => Key.OemComma,
        Keycode.Minus => Key.OemMinus,
        Keycode.Period => Key.OemPeriod,
        Keycode.Slash => Key.OemQuestion,
        Keycode.Grave => Key.OemTilde,
        Keycode.LeftBracket => Key.OemOpenBrackets,
        Keycode.Backslash => Key.OemPipe,
        Keycode.RightBracket => Key.OemCloseBrackets,
        Keycode.Apostrophe => Key.OemQuotes,

        Keycode.NumpadAdd => Key.Add,
        Keycode.NumpadSubtract => Key.Subtract,
        Keycode.NumpadMultiply => Key.Multiply,
        Keycode.NumpadDivide => Key.Divide,
        Keycode.NumpadDot => Key.Decimal,
        Keycode.NumpadComma => Key.Separator,

        _ => Key.None,
    };

    public static KeyModifiers ToModifiers(MetaKeyStates meta)
    {
        KeyModifiers modifiers = KeyModifiers.None;

        if ((meta & MetaKeyStates.ShiftOn) != 0) modifiers |= KeyModifiers.Shift;
        if ((meta & MetaKeyStates.CtrlOn) != 0) modifiers |= KeyModifiers.Control;
        if ((meta & MetaKeyStates.AltOn) != 0) modifiers |= KeyModifiers.Alt;

        return modifiers;
    }
}