using System.Globalization;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Map;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>A map of OpenStreetMap tiles with markers.</summary>
internal static class MapPage
{
    private static readonly (string Name, double Latitude, double Longitude)[] Cities =
    [
        ("Moscow", 55.751244, 37.618423),
        ("Saint Petersburg", 59.938732, 30.316229),
        ("Kazan", 55.796127, 49.106405),
        ("Novosibirsk", 55.030199, 82.920430),
        ("Irkutsk", 52.286387, 104.280660),
    ];

    public static UIElement Create()
    {
        Label status = Demo.Note("Drag to pan, the wheel or a pinch zooms; click a marker");

        var map = new MapControl
        {
            // the tile servers ask every application to name itself
            UserAgent = $"ZeppelinForms-Showcase/{HomePage.FrameworkVersion}",
            ShowCoordinates = true,
            Size = new(float.NaN, 440),
        };

        foreach (var (name, latitude, longitude) in Cities)
            map.Markers.Add(new MapMarker(latitude, longitude, name));

        map.GoTo(Cities[0].Latitude, Cities[0].Longitude, zoom: 5);

        map.MarkerClicked += (_, marker) =>
        {
            status.Text = $"{marker.Label}: {marker.Latitude.ToString("0.000", CultureInfo.InvariantCulture)}, {marker.Longitude.ToString("0.000", CultureInfo.InvariantCulture)}";
            map.GoTo(marker.Latitude, marker.Longitude, zoom: 11);
        };

        var all = new Button { Text = "Show all" };
        all.Click += (_, _) => map.FitMarkers();

        var goTo = new ComboBox { PlaceholderText = "Go to a city" };
        goTo.Items.AddRange(Cities.Select(c => c.Name));
        goTo.SelectionChanged += (_, _) =>
        {
            if (goTo.SelectedIndex < 0) return;
            var target = Cities[goTo.SelectedIndex];
            map.GoTo(target.Latitude, target.Longitude, zoom: 11);
        };

        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, CrossAxisAlignment = CrossAxisAlignment.Stretch };
        root.Children.AddRange([Demo.Row(goTo, all), map, status]);

        return Demo.Page("Map", "OpenStreetMap tiles, loaded as you move, with markers on top.",
            Demo.Section("Map control", root, "Tiles are cached on disk; the attribution stays in the corner as the license asks"));
    }
}
