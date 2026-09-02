using System;
using System.Collections.Generic;
using System.Text;

namespace ZeppelinForms.Forms.Controls.Map;

public sealed record TileSource(string UrlTemplate, string Attribution, int MinZoom = 0, int MaxZoom = 19)
{
    public string BuildUrl(int x, int y, int zoom) => UrlTemplate
        .Replace("{z}", zoom.ToString())
        .Replace("{x}", x.ToString())
        .Replace("{y}", y.ToString());

    public static TileSource OpenStreetMap { get; } = new(
        "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
        "© OpenStreetMap contributors");
}
