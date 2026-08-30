using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls.Charts;

public static class ChartPalette
{
    private static readonly Color[] Colors =
    [
        new(255, 0x0D, 0x6E, 0xFD),
    new(255, 0xDC, 0x35, 0x45),
    new(255, 0x19, 0x87, 0x54),
    new(255, 0xFF, 0xC1, 0x07),
    new(255, 0x6F, 0x42, 0xC1),
    new(255, 0x20, 0xC9, 0x97),
    new(255, 0xFD, 0x7E, 0x14),
    new(255, 0x6C, 0x75, 0x7D),
];

    public static Color At(int index) => Colors[index % Colors.Length];
}
