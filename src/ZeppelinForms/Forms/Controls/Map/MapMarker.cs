using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls.Map;

public sealed class MapMarker(double latitude, double longitude, string? label = null)
{
    public double Latitude { get; set; } = latitude;
    public double Longitude { get; set; } = longitude;
    public string? Label { get; set; } = label;

    public Color Color { get; set; } = new Color(255, 0xDC, 0x35, 0x45);
    public string? PathData { get; set; }
    public object? Tag { get; set; }
}