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