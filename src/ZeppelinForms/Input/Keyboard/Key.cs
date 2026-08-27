using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Core;

namespace ZeppelinForms.Input.Keyboard;

// Значения совпадают с Win32 Virtual-Key Codes, чтобы приведение
// из wParam было прямым, без таблицы соответствия.
public enum Key : int
{
    None = 0,
    Tab = 0x09,
    Enter = 0x0D,
    Escape = 0x1B,
    Space = 0x20,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    Delete = 0x2E,
    F1 = 0x70,
    F2 = 0x71,
    F3 = 0x72,
    F4 = 0x73,
    F5 = 0x74,
    F6 = 0x75,
    F7 = 0x76,
    F8 = 0x77,
    F9 = 0x78,
    F10 = 0x79,
    F11 = 0x7A,
    F12 = 0x7B,
}

public sealed record class KeyEventArgs(Key Key) : ZfEventArgs
{
    public bool Handled { get; set; }
}
