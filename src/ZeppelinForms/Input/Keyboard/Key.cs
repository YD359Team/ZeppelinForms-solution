using System;
using System.Collections.Generic;
using System.Text;

namespace ZeppelinForms.Input.Keyboard;

// Значения совпадают с Win32 Virtual-Key Codes, чтобы приведение
// из wParam было прямым, без таблицы соответствия.
public enum Key : int
{
    None = 0,

    // управление
    Back = 0x08,
    Tab = 0x09,
    Clear = 0x0C,
    Enter = 0x0D,
    Pause = 0x13,
    CapsLock = 0x14,
    Escape = 0x1B,
    Space = 0x20,

    // навигация
    PageUp = 0x21,
    PageDown = 0x22,
    End = 0x23,
    Home = 0x24,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    PrintScreen = 0x2C,
    Insert = 0x2D,
    Delete = 0x2E,

    // цифровой ряд
    D0 = 0x30, D1, D2, D3, D4, D5, D6, D7, D8, D9,

    // буквы
    A = 0x41, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    // системные клавиши
    LeftWindows = 0x5B,
    RightWindows = 0x5C,
    Apps = 0x5D,

    // цифровой блок
    NumPad0 = 0x60, NumPad1, NumPad2, NumPad3, NumPad4,
    NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
    Multiply = 0x6A,
    Add = 0x6B,
    Separator = 0x6C,
    Subtract = 0x6D,
    Decimal = 0x6E,
    Divide = 0x6F,

    F1 = 0x70, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,

    NumLock = 0x90,
    ScrollLock = 0x91,

    // конкретная сторона: приходит там, где система различает левую и правую.
    // Обобщённые Shift/Control/Alt ниже — для случаев, когда не различает
    LeftShift = 0xA0,
    RightShift = 0xA1,
    LeftControl = 0xA2,
    RightControl = 0xA3,
    LeftAlt = 0xA4,
    RightAlt = 0xA5,

    Shift = 0x10,
    Control = 0x11,
    Alt = 0x12,

    // знаки: раскладка на них влияет, поэтому имена по физическому
    // положению на американской клавиатуре, как это принято в Win32
    OemSemicolon = 0xBA,      // ;
    OemPlus = 0xBB,           // =
    OemComma = 0xBC,          // ,
    OemMinus = 0xBD,          // -
    OemPeriod = 0xBE,         // .
    OemQuestion = 0xBF,       // /
    OemTilde = 0xC0,          // `
    OemOpenBrackets = 0xDB,   // [
    OemPipe = 0xDC,           // \
    OemCloseBrackets = 0xDD,  // ]
    OemQuotes = 0xDE,         // '
    OemBackslash = 0xE2,      // < > на 102-клавишной
}
