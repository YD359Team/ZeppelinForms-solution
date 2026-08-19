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
}

public static class Colors
{
    public static readonly Color Transparent = new Color(0, 0, 0, 0);
    public static readonly Color Black = new Color(255, 0, 0, 0);
    public static readonly Color White = new Color(255, 255, 255, 255);
}
