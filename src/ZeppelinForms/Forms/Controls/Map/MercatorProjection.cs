namespace ZeppelinForms.Forms.Controls.Map;

/// <summary>
/// Веб-Меркатор: перевод географических координат в пиксели «мирового»
/// полотна заданного зума и обратно.
/// </summary>
internal static class MercatorProjection
{
    public const int TileSize = 256;
    public const double MaxLatitude = 85.05112878;

    public static double WorldSize(int zoom) => TileSize * Math.Pow(2, zoom);

    public static (double X, double Y) ToWorld(double latitude, double longitude, int zoom)
    {
        double size = WorldSize(zoom);
        double lat = Math.Clamp(latitude, -MaxLatitude, MaxLatitude) * Math.PI / 180d;

        double x = (longitude + 180d) / 360d * size;
        double y = (1d - Math.Log(Math.Tan(lat) + 1d / Math.Cos(lat)) / Math.PI) / 2d * size;

        return (x, y);
    }

    public static (double Latitude, double Longitude) ToGeo(double x, double y, int zoom)
    {
        double size = WorldSize(zoom);

        double longitude = x / size * 360d - 180d;

        double n = Math.PI - 2d * Math.PI * y / size;
        double latitude = 180d / Math.PI * Math.Atan(Math.Sinh(n));

        return (latitude, longitude);
    }
}
