using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing.Helpers;

public static class ColorExtensions
{
    public static Color Darken(this Color c, float amount = 0.15f)
    {
        byte Adjust(byte v) => (byte)Math.Clamp(v * (1 - amount), 0, 255);
        return new Color(c.A, Adjust(c.R), Adjust(c.G), Adjust(c.B));
    }

    public static Color Lighten(this Color c, float amount = 0.15f)
    {
        byte Adjust(byte v) => (byte)Math.Clamp(v + (255 - v) * amount, 0, 255);
        return new Color(c.A, Adjust(c.R), Adjust(c.G), Adjust(c.B));
    }
}
