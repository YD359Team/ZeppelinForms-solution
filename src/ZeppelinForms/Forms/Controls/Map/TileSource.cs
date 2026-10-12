using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZeppelinForms.Forms.Controls.Map;

public sealed record TileSource(string UrlTemplate, string Attribution, int MinZoom = 0, int MaxZoom = 19)
{
    public string BuildUrl(int x, int y, int zoom) => UrlTemplate
        // a URL: digits the server reads, whatever the user's culture
        .Replace("{z}", zoom.ToString(CultureInfo.InvariantCulture))
        .Replace("{x}", x.ToString(CultureInfo.InvariantCulture))
        .Replace("{y}", y.ToString(CultureInfo.InvariantCulture));

    public static TileSource OpenStreetMap { get; } = new(
        "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
        "© OpenStreetMap contributors");
}
