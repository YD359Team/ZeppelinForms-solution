using System;
using System.Collections.Generic;
using System.Text;

namespace ZeppelinForms.Drawing.Primitives;

public readonly record struct Color
{
    public readonly byte A { get; }
    public readonly byte R { get; }
    public readonly byte G { get; }
    public readonly byte B { get; }

    public Color(byte a, byte r, byte g, byte b)
    {
        A = a; R = r; G = g; B = b;
    }

    public Color(byte r, byte g, byte b)
    {
        A = 255; R = r; G = g; B = b;
    }

    public readonly uint AsU32()
    {
        return (uint)((A << 24) | (R << 16) | (G << 8) | B);
    }
}

public static class Colors
{
    public static readonly Color Transparent = new(0, 0, 0, 0);
    public static readonly Color Black = new(255, 0, 0, 0);
    public static readonly Color White = new(255, 255, 255, 255);
    public static readonly Color Red = new(255, 255, 0, 0);
    public static readonly Color Blue = new(255, 0, 0, 255);
}

public static class LightThemeColors
{
    public static readonly Color Background = new(0xFF, 0xFF, 0xFF);
    public static readonly Color Text = new(0x00, 0x00, 0x00);
    public static readonly Color ButtonFill = new(0x0D, 0x6E, 0xFD);
    public static readonly Color AccentBackground = new(0xF8, 0xF9, 0xFA);
    public static readonly Color AccentFill = new(0x0D, 0x6E, 0xFD);
}
