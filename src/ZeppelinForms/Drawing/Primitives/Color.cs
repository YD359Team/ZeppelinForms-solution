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

    public static Color Lerp(Color a, Color b, float t) 
    { 
        t = Math.Clamp(t, 0f, 1f); 
        return new(
            (byte)(a.A + (b.A - a.A) * t), 
            (byte)(a.R + (b.R - a.R) * t), 
            (byte)(a.G + (b.G - a.G) * t), 
            (byte)(a.B + (b.B - a.B) * t)
        ); 
    }
}
