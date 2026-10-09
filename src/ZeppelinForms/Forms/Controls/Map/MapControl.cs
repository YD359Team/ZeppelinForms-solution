using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls.Map;

/// <summary>
/// A map on raster tiles. OpenStreetMap by default.
/// </summary>
public partial class MapControl : InteractiveControl
{
    private const int TileSize = MercatorProjection.TileSize;

    private static readonly Lazy<HttpClient> Http = new(() => new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    });

    private readonly TileCache _cache = new();
    private readonly string _diskCacheDirectory;

    private double _centerLatitude = 55.751244;
    private double _centerLongitude = 37.618423;
    private int _zoom = 10;

    private bool _isDragging;
    private Point _dragStart;
    private (double X, double Y) _dragStartWorld;

    private MapMarker? _hoveredMarker;

    // ===== map state =====

    /// <summary>Where the tiles come from.</summary>
    /// <remarks>
    /// The memory cache keys tiles by position, not by source, so a new source
    /// clears it: without that the old source's tiles kept showing under the new one
    /// until they were evicted.
    /// </remarks>
    public TileSource Source
    {
        get;
        set
        {
            if (Equals(field, value)) return;

            field = value;

            _cache.Clear();
            _zoom = Math.Clamp(_zoom, value.MinZoom, value.MaxZoom);

            InvalidateVisual();
        }
    } = TileSource.OpenStreetMap;

    public string UserAgent { get; set; } = "ZeppelinForms/0.3 (https://github.com/YD359Team)";

    public double CenterLatitude => _centerLatitude;
    public double CenterLongitude => _centerLongitude;
    public int Zoom => _zoom;

    public List<MapMarker> Markers { get; init; } = [];

    public bool ShowCoordinates { get; set; }
    public bool ShowAttribution { get; set; } = true;

    /// <summary>The backing of the labels drawn over the map. A styled property:
    /// the text takes the theme's color, so its backing must follow the theme too.</summary>
    [Styled(Category = "Map")]
    public partial Color TextBackground { get; set; }
    private static Color TextBackgroundDefault => new(190, 255, 255, 255);

    public event EventHandler? ViewChanged;
    public event EventHandler<MapMarker>? MarkerClicked;

    public MapControl()
    {
        SetControlDefault(BackgroundProperty, new Color(255, 0xE8, 0xE8, 0xE8));
        Cursor = CursorKind.SizeAll;
        SetControlDefault(BorderColorProperty, new Color(255, 190, 190, 190));
        SetControlDefault(BorderWidthProperty, 1f);

        _diskCacheDirectory = Path.Combine(Path.GetTempPath(), "ZeppelinForms", "MapTiles");
        Directory.CreateDirectory(_diskCacheDirectory);
    }

    public void GoTo(double latitude, double longitude, int? zoom = null)
    {
        _centerLatitude = Math.Clamp(latitude, -MercatorProjection.MaxLatitude, MercatorProjection.MaxLatitude);
        _centerLongitude = NormalizeLongitude(longitude);

        if (zoom is int value)
            _zoom = Math.Clamp(value, Source.MinZoom, Source.MaxZoom);

        ViewChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void SetZoom(int zoom) => GoTo(_centerLatitude, _centerLongitude, zoom);

    /// <summary>Pick the center and zoom so that all markers fit in the frame.</summary>
    public void FitMarkers(float paddingPixels = 40f)
    {
        if (Markers.Count == 0) return;

        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLon = double.MaxValue, maxLon = double.MinValue;

        foreach (MapMarker marker in Markers)
        {
            minLat = Math.Min(minLat, marker.Latitude);
            maxLat = Math.Max(maxLat, marker.Latitude);
            minLon = Math.Min(minLon, marker.Longitude);
            maxLon = Math.Max(maxLon, marker.Longitude);
        }

        double centerLat = (minLat + maxLat) / 2;
        double centerLon = (minLon + maxLon) / 2;

        // pick the largest zoom at which the markers' box still fits
        int best = Source.MinZoom;

        for (int zoom = Source.MaxZoom; zoom >= Source.MinZoom; zoom--)
        {
            var (x1, y1) = MercatorProjection.ToWorld(maxLat, minLon, zoom);
            var (x2, y2) = MercatorProjection.ToWorld(minLat, maxLon, zoom);

            if (Math.Abs(x2 - x1) + paddingPixels * 2 <= ContentBounds.Width &&
                Math.Abs(y2 - y1) + paddingPixels * 2 <= ContentBounds.Height)
            {
                best = zoom;
                break;
            }
        }

        GoTo(centerLat, centerLon, best);
    }

    // ===== coordinate conversion =====

    /// <summary>A geographic point in the control's own coordinates.</summary>
    public Point GeoToScreen(double latitude, double longitude)
    {
        Rectangle content = ContentBounds;

        var (centerX, centerY) = MercatorProjection.ToWorld(_centerLatitude, _centerLongitude, _zoom);
        var (pointX, pointY) = MercatorProjection.ToWorld(latitude, longitude, _zoom);

        return new Point(
            (float)(content.X + content.Width / 2 + (pointX - centerX)),
            (float)(content.Y + content.Height / 2 + (pointY - centerY)));
    }

    /// <summary>A point in the control's own coordinates as a geographic point.</summary>
    public (double Latitude, double Longitude) ScreenToGeo(Point screen)
    {
        Rectangle content = ContentBounds;

        var (centerX, centerY) = MercatorProjection.ToWorld(_centerLatitude, _centerLongitude, _zoom);

        double x = centerX + (screen.X - content.X - content.Width / 2);
        double y = centerY + (screen.Y - content.Y - content.Height / 2);

        return MercatorProjection.ToGeo(x, y, _zoom);
    }

    /// <summary>A window point from an input event in the control's own coordinates —
    /// the space GeoToScreen and ScreenToGeo work in.</summary>
    /// <remarks>
    /// Mouse events come in window coordinates. Marker hit testing and zooming to
    /// the cursor used to pass them on as they were, so on a map that doesn't sit
    /// in the window's top-left corner they missed by the map's own offset.
    /// </remarks>
    private Point ToLocal(Point location)
    {
        Point abs = GetAbsolutePosition();

        return new Point(location.X - abs.X, location.Y - abs.Y);
    }

    // ===== drawing =====

    protected override void DrawContent(Graphics g)
    {
        Rectangle content = ContentBounds;

        if (content.Width <= 0 || content.Height <= 0) return;

        g.Save();
        g.ClipRect(content);

        DrawTiles(g, content);
        DrawMarkers(g);

        g.Restore();

        if (ShowCoordinates)
            DrawCoordinates(g, content);

        if (ShowAttribution)
            DrawAttribution(g, content);
    }

    private void DrawTiles(Graphics g, Rectangle content)
    {
        var (centerX, centerY) = MercatorProjection.ToWorld(_centerLatitude, _centerLongitude, _zoom);

        // world coordinates of the top-left corner of the visible area
        double originX = centerX - content.Width / 2;
        double originY = centerY - content.Height / 2;

        int firstTileX = (int)Math.Floor(originX / TileSize);
        int firstTileY = (int)Math.Floor(originY / TileSize);

        int columns = (int)Math.Ceiling(content.Width / TileSize) + 1;
        int rows = (int)Math.Ceiling(content.Height / TileSize) + 1;

        int worldTiles = 1 << _zoom;

        for (int row = 0; row <= rows; row++)
        {
            int tileY = firstTileY + row;

            // vertically the world is not closed: there are no tiles beyond the poles
            if (tileY < 0 || tileY >= worldTiles) continue;

            for (int column = 0; column <= columns; column++)
            {
                int tileX = firstTileX + column;

                // horizontally the world is closed, so the index is wrapped
                int normalizedX = ((tileX % worldTiles) + worldTiles) % worldTiles;

                var destination = new Rectangle(
                    new Point(
                        (float)(content.X + tileX * (double)TileSize - originX),
                        (float)(content.Y + tileY * (double)TileSize - originY)),
                    new Size(TileSize, TileSize));

                if (_cache.TryGet(_zoom, normalizedX, tileY, out Image? tile) && tile is not null)
                    g.DrawImage(destination, tile);
                else
                    RequestTile(normalizedX, tileY, _zoom);
            }
        }
    }

    private void DrawMarkers(Graphics g)
    {
        foreach (MapMarker marker in Markers)
        {
            Point screen = GeoToScreen(marker.Latitude, marker.Longitude);

            bool hovered = ReferenceEquals(marker, _hoveredMarker);
            float size = hovered ? 26f : 22f;

            if (!string.IsNullOrEmpty(marker.PathData))
            {
                g.DrawSvgPath(marker.PathData,
                    new Rectangle(new Point(screen.X - size / 2, screen.Y - size), new Size(size, size)),
                    marker.Color);
            }
            else
            {
                // a pin: a circle with a "nose" pointing down, the tip at the coordinates
                float radius = size / 2;

                ReadOnlySpan<Point> tip =
                [
                    new(screen.X - radius * 0.5f, screen.Y - radius * 0.9f),
                    new(screen.X, screen.Y),
                    new(screen.X + radius * 0.5f, screen.Y - radius * 0.9f),
                ];

                g.DrawPolyline(tip, marker.Color, radius * 0.9f);

                g.FillEllipse(
                    new Rectangle(new Point(screen.X - radius, screen.Y - size), new Size(size, size)),
                    marker.Color);

                g.FillEllipse(
                    new Rectangle(new Point(screen.X - radius * 0.35f, screen.Y - size * 0.72f),
                        new Size(radius * 0.7f, radius * 0.7f)),
                    Colors.White);
            }

            if (!hovered || string.IsNullOrEmpty(marker.Label)) continue;

            Size labelSize = TextMeasurer.Current.MeasureText(marker.Label, EffectiveFont);

            var labelRect = new Rectangle(
                new Point(screen.X - labelSize.Width / 2 - 6, screen.Y - size - labelSize.Height - 8),
                new Size(labelSize.Width + 12, labelSize.Height + 6));

            g.FillRoundRectangle(labelRect, new CornerRadius(3f), TextBackground);

            g.DrawText(marker.Label, labelRect, TextColor, EffectiveFont,
                HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
        }
    }

    private void DrawCoordinates(Graphics g, Rectangle content)
    {
        string text = $"{_centerLatitude:F5}, {_centerLongitude:F5}  z{_zoom}";
        Size size = TextMeasurer.Current.MeasureText(text, EffectiveFont);

        var rect = new Rectangle(
            new Point(content.X + 6, content.Y + 6),
            new Size(size.Width + 12, size.Height + 6));

        g.FillRoundRectangle(rect, new CornerRadius(3f), TextBackground);

        g.DrawText(text, rect, TextColor, EffectiveFont,
            HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
    }

    private void DrawAttribution(Graphics g, Rectangle content)
    {
        // the ODbL license requires naming the data source
        Size size = TextMeasurer.Current.MeasureText(Source.Attribution, EffectiveFont);

        var rect = new Rectangle(
            new Point(content.X + content.Width - size.Width - 12, content.Y + content.Height - size.Height - 8),
            new Size(size.Width + 10, size.Height + 4));

        g.FillRoundRectangle(rect, new CornerRadius(2f), TextBackground);

        g.DrawText(Source.Attribution, rect, TextColor, EffectiveFont,
            HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
    }

    // ===== tile loading =====

    private void RequestTile(int x, int y, int zoom)
    {
        // a second request for the same tile is not needed: while panning
        // the same tile gets into the frame dozens of times in a row
        if (!_cache.TryBeginLoad(zoom, x, y)) return;

        // the source is taken here, on the UI thread, once: the load continues
        // in the background, and reading Source after an await would see
        // whatever it is by then
        _ = LoadTileAsync(x, y, zoom, Source);
    }

    private async Task LoadTileAsync(int x, int y, int zoom, TileSource source)
    {
        try
        {
            string path = Path.Combine(_diskCacheDirectory, $"{StableKey(source)}_{zoom}_{x}_{y}.png");

            byte[] data;

            if (File.Exists(path))
            {
                data = await File.ReadAllBytesAsync(path);
            }
            else
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, source.BuildUrl(x, y, zoom));
                request.Headers.Add("User-Agent", UserAgent);

                using HttpResponseMessage response = await Http.Value.SendAsync(request);
                response.EnsureSuccessStatusCode();

                data = await response.Content.ReadAsByteArrayAsync();

                await File.WriteAllBytesAsync(path, data);
            }

            using var stream = new MemoryStream(data);
            Image tile = Image.Load(stream);

            // the source changed while the tile was on its way: it belongs to the old
            // map and must not get into the cache under the same position
            if (Equals(source, Source))
                _cache.Put(zoom, x, y, tile);

            // decoding went on in the background, and the control tree is touched
            // only on the UI thread. A redraw is requested even for a discarded tile:
            // it makes the current source's request go out for this position
            FindOwner()?.BeginInvoke(InvalidateVisual);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Tile {zoom}/{x}/{y} was not loaded: {ex.Message}");
        }
        finally
        {
            _cache.EndLoad(zoom, x, y);
        }
    }

    /// <summary>A key of the source for the disk cache file names, the same on every run.</summary>
    /// <remarks>
    /// The names used to be built from Source.GetHashCode(). TileSource is a record of
    /// strings, and string hashing in .NET is randomized per process — so every run got
    /// new names: the disk cache never hit, and the temp folder only grew. FNV-1a over
    /// the URL template is stable across runs and machines.
    /// </remarks>
    private static string StableKey(TileSource source)
    {
        uint hash = 2166136261;

        foreach (char c in source.UrlTemplate)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return hash.ToString("X8");
    }

    // ===== input =====

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (!_isDragging)
        {
            UpdateHover(MarkerAt(ToLocal(e.Location)));
            return;
        }

        // the center is moved in projected pixels, not in degrees:
        // in Mercator the degrees of latitude per pixel depend on the latitude itself.
        // Differences between two window points need no conversion to local ones
        double x = _dragStartWorld.X - (e.Location.X - _dragStart.X);
        double y = _dragStartWorld.Y - (e.Location.Y - _dragStart.Y);

        y = Math.Clamp(y, 0, MercatorProjection.WorldSize(_zoom));

        var (latitude, longitude) = MercatorProjection.ToGeo(x, y, _zoom);

        GoTo(latitude, longitude);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.Button != MouseButton.Left) return;

        MapMarker? marker = MarkerAt(ToLocal(e.Location));

        if (marker is not null)
        {
            MarkerClicked?.Invoke(this, marker);
            e.Handled = true;
            return;
        }

        _isDragging = true;
        _dragStart = e.Location;
        _dragStartWorld = MercatorProjection.ToWorld(_centerLatitude, _centerLongitude, _zoom);

        // without capture the drag breaks off as soon as the cursor leaves the window
        CaptureMouse();
    }

    /// <summary>The cursor left the map: the hovered marker and its label go away.
    /// This used to repeat OnMouseMove and looked for a marker at the exit point,
    /// so a label could stay hanging after the mouse had left.</summary>
    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (_isDragging) return;

        UpdateHover(null);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (!_isDragging) return;

        _isDragging = false;
        ReleaseMouseCapture();
    }

    /// <summary>The drag was cut off. Previously _isDragging stayed true, and moving
    /// the mouse with no button pressed kept panning the map. The view isn't rolled
    /// back: panning commits nothing, the map simply stays where it was left.</summary>
    protected override void OnPointerCanceled(PointerCancelEventArgs e) => _isDragging = false;

    private void UpdateHover(MapMarker? marker)
    {
        if (ReferenceEquals(marker, _hoveredMarker)) return;

        _hoveredMarker = marker;
        Cursor = marker is not null ? CursorKind.Hand : CursorKind.SizeAll;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // the form delivers the wheel to disabled elements too
        if (!IsEnabled) return;

        int target = Math.Clamp(_zoom + Math.Sign(e.Delta), Source.MinZoom, Source.MaxZoom);

        if (target == _zoom)
        {
            e.Handled = true;
            return;
        }

        // the point under the cursor must stay in place: the center is recomputed
        // so that its screen offset from the center is preserved
        Rectangle content = ContentBounds;
        Point local = ToLocal(e.Location);

        double offsetX = local.X - content.X - content.Width / 2;
        double offsetY = local.Y - content.Y - content.Height / 2;

        var (cursorLatitude, cursorLongitude) = ScreenToGeo(local);
        var (cursorX, cursorY) = MercatorProjection.ToWorld(cursorLatitude, cursorLongitude, target);

        var (latitude, longitude) = MercatorProjection.ToGeo(cursorX - offsetX, cursorY - offsetY, target);

        _zoom = target;
        GoTo(latitude, longitude);

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        const float step = 80f;

        switch (e.Key)
        {
            case Key.Left: PanByPixels(-step, 0); break;
            case Key.Right: PanByPixels(step, 0); break;
            case Key.Up: PanByPixels(0, -step); break;
            case Key.Down: PanByPixels(0, step); break;
            default: return;
        }

        e.Handled = true;
    }

    private void PanByPixels(float dx, float dy)
    {
        var (x, y) = MercatorProjection.ToWorld(_centerLatitude, _centerLongitude, _zoom);
        var (latitude, longitude) = MercatorProjection.ToGeo(x + dx, y + dy, _zoom);

        GoTo(latitude, longitude);
    }

    /// <summary>The marker at a point in the control's own coordinates.</summary>
    private MapMarker? MarkerAt(Point local)
    {
        const float radius = 14f;

        // from the end: the last marker is drawn on top of the others
        for (int i = Markers.Count - 1; i >= 0; i--)
        {
            Point screen = GeoToScreen(Markers[i].Latitude, Markers[i].Longitude);

            float dx = local.X - screen.X;
            float dy = local.Y - screen.Y + radius;

            if (dx * dx + dy * dy <= radius * radius)
                return Markers[i];
        }

        return null;
    }

    private static double NormalizeLongitude(double longitude) =>
        ((longitude + 180d) % 360d + 360d) % 360d - 180d;

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(new Size(400 + Padding.Horizontal, 300 + Padding.Vertical), availableSize);
}