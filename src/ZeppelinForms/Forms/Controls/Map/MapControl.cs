using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Map;

/// <summary>
/// Карта на растровых тайлах. По умолчанию OpenStreetMap.
/// </summary>
public class MapControl : UnitControl, IInputElement, IBorderedElement
{
    private const int TileSize = MercatorProjection.TileSize;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private readonly TileCache _cache = new();
    private readonly string _diskCacheDirectory;

    private double _centerLatitude = 55.751244;
    private double _centerLongitude = 37.618423;
    private int _zoom = 10;

    private bool _isDragging;
    private Point _dragStart;
    private (double X, double Y) _dragStartWorld;

    private MapMarker? _hoveredMarker;

    public MapControl()
    {
        Background = new Color(255, 0xE8, 0xE8, 0xE8);
        Cursor = CursorKind.SizeAll;

        _diskCacheDirectory = Path.Combine(Path.GetTempPath(), "ZeppelinForms", "MapTiles");
        Directory.CreateDirectory(_diskCacheDirectory);
    }

    // ===== состояние карты =====

    public TileSource Source { get; set; } = TileSource.OpenStreetMap;

    public string UserAgent { get; set; } = "ZeppelinForms/0.3 (https://github.com/YD359Team)";

    public double CenterLatitude => _centerLatitude;
    public double CenterLongitude => _centerLongitude;
    public int Zoom => _zoom;

    public List<MapMarker> Markers { get; init; } = [];

    public bool ShowCoordinates { get; set; }
    public bool ShowAttribution { get; set; } = true;

    public Color TextColor { get; set; } = new Color(255, 60, 60, 60);
    public Color TextBackground { get; set; } = new Color(190, 255, 255, 255);

    public Color BorderColor { get; set; } = new Color(255, 190, 190, 190);
    public float BorderWidth { get; set; } = 1f;

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    public event EventHandler? ViewChanged;
    public event EventHandler<MapMarker>? MarkerClicked;

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

    /// <summary>Подобрать центр и зум так, чтобы все метки попали в кадр.</summary>
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

        // подбираем наибольший зум, при котором рамка меток ещё влезает
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

    // ===== перевод координат =====

    public Point GeoToScreen(double latitude, double longitude)
    {
        Rectangle content = ContentBounds;

        var (centerX, centerY) = MercatorProjection.ToWorld(_centerLatitude, _centerLongitude, _zoom);
        var (pointX, pointY) = MercatorProjection.ToWorld(latitude, longitude, _zoom);

        return new Point(
            (float)(content.X + content.Width / 2 + (pointX - centerX)),
            (float)(content.Y + content.Height / 2 + (pointY - centerY)));
    }

    public (double Latitude, double Longitude) ScreenToGeo(Point screen)
    {
        Rectangle content = ContentBounds;

        var (centerX, centerY) = MercatorProjection.ToWorld(_centerLatitude, _centerLongitude, _zoom);

        double x = centerX + (screen.X - content.X - content.Width / 2);
        double y = centerY + (screen.Y - content.Y - content.Height / 2);

        return MercatorProjection.ToGeo(x, y, _zoom);
    }

    // ===== отрисовка =====

    public override void Draw(Graphics g)
    {
        Rectangle content = ContentBounds;

        if (Background.A > 0)
            g.FillRoundRectangle(LocalBounds, CornerRadius, Background);

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

        if (BorderWidth > 0)
            g.DrawRoundRectangle(LocalBounds, CornerRadius, BorderColor, BorderWidth);
    }

    private void DrawTiles(Graphics g, Rectangle content)
    {
        var (centerX, centerY) = MercatorProjection.ToWorld(_centerLatitude, _centerLongitude, _zoom);

        // мировые координаты левого верхнего угла видимой области
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

            // по вертикали мир не замкнут: за полюсами тайлов нет
            if (tileY < 0 || tileY >= worldTiles) continue;

            for (int column = 0; column <= columns; column++)
            {
                int tileX = firstTileX + column;

                // по горизонтали мир замкнут, поэтому индекс сворачиваем
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
                // булавка: круг с «носиком» вниз, острие в точке координат
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
        // лицензия ODbL требует указывать источник данных
        Size size = TextMeasurer.Current.MeasureText(Source.Attribution, EffectiveFont);

        var rect = new Rectangle(
            new Point(content.X + content.Width - size.Width - 12, content.Y + content.Height - size.Height - 8),
            new Size(size.Width + 10, size.Height + 4));

        g.FillRoundRectangle(rect, new CornerRadius(2f), TextBackground);

        g.DrawText(Source.Attribution, rect, TextColor, EffectiveFont,
            HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
    }

    // ===== загрузка тайлов =====

    private void RequestTile(int x, int y, int zoom)
    {
        // второй запрос того же тайла не нужен: при панорамировании
        // один и тот же тайл попадает в кадр десятки раз подряд
        if (!_cache.TryBeginLoad(zoom, x, y)) return;

        _ = LoadTileAsync(x, y, zoom);
    }

    private async Task LoadTileAsync(int x, int y, int zoom)
    {
        try
        {
            string path = Path.Combine(_diskCacheDirectory, $"{Source.GetHashCode():X8}_{zoom}_{x}_{y}.png");

            byte[] data;

            if (File.Exists(path))
            {
                data = await File.ReadAllBytesAsync(path);
            }
            else
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, Source.BuildUrl(x, y, zoom));
                request.Headers.Add("User-Agent", UserAgent);

                using HttpResponseMessage response = await Http.SendAsync(request);
                response.EnsureSuccessStatusCode();

                data = await response.Content.ReadAsByteArrayAsync();

                await File.WriteAllBytesAsync(path, data);
            }

            using var stream = new MemoryStream(data);
            Image tile = Image.Load(stream);

            _cache.Put(zoom, x, y, tile);

            // декодирование шло в фоне, а дерево контролов трогаем
            // только на потоке интерфейса
            FindOwner()?.Invoke(InvalidateVisual);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Тайл {zoom}/{x}/{y} не загружен: {ex.Message}");
        }
        finally
        {
            _cache.EndLoad(zoom, x, y);
        }
    }

    // ===== ввод =====

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (!_isDragging)
        {
            MapMarker? marker = MarkerAt(e.Location);

            if (!ReferenceEquals(marker, _hoveredMarker))
            {
                _hoveredMarker = marker;
                Cursor = marker is not null ? CursorKind.Hand : CursorKind.SizeAll;
                InvalidateVisual();
            }

            return;
        }

        // сдвигаем центр в проецированных пикселях, а не в градусах:
        // в Меркаторе градус широты на пиксель зависит от самой широты
        double x = _dragStartWorld.X - (e.Location.X - _dragStart.X);
        double y = _dragStartWorld.Y - (e.Location.Y - _dragStart.Y);

        y = Math.Clamp(y, 0, MercatorProjection.WorldSize(_zoom));

        var (latitude, longitude) = MercatorProjection.ToGeo(x, y, _zoom);

        GoTo(latitude, longitude);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.Button != MouseButton.Left) return;

        MapMarker? marker = MarkerAt(e.Location);

        if (marker is not null)
        {
            MarkerClicked?.Invoke(this, marker);
            e.Handled = true;
            return;
        }

        _isDragging = true;
        _dragStart = e.Location;
        _dragStartWorld = MercatorProjection.ToWorld(_centerLatitude, _centerLongitude, _zoom);
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (!_isDragging)
        {
            MapMarker? marker = MarkerAt(e.Location);

            if (!ReferenceEquals(marker, _hoveredMarker))
            {
                _hoveredMarker = marker;
                Cursor = marker is not null ? CursorKind.Hand : CursorKind.SizeAll;
                InvalidateVisual();
            }

            return;
        }

        double x = _dragStartWorld.X - (e.Location.X - _dragStart.X);
        double y = _dragStartWorld.Y - (e.Location.Y - _dragStart.Y);

        y = Math.Clamp(y, 0, MercatorProjection.WorldSize(_zoom));

        var (latitude, longitude) = MercatorProjection.ToGeo(x, y, _zoom);

        GoTo(latitude, longitude);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e) => _isDragging = false;

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        int target = Math.Clamp(_zoom + Math.Sign(e.Delta), Source.MinZoom, Source.MaxZoom);

        if (target == _zoom)
        {
            e.Handled = true;
            return;
        }

        // точка под курсором должна остаться на месте: пересчитываем центр
        // так, чтобы её экранное смещение от центра сохранилось
        Rectangle content = ContentBounds;

        double offsetX = e.Location.X - content.X - content.Width / 2;
        double offsetY = e.Location.Y - content.Y - content.Height / 2;

        var (cursorLatitude, cursorLongitude) = ScreenToGeo(e.Location);
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

    private MapMarker? MarkerAt(Point location)
    {
        const float radius = 14f;

        // с конца: последняя метка рисуется поверх остальных
        for (int i = Markers.Count - 1; i >= 0; i--)
        {
            Point screen = GeoToScreen(Markers[i].Latitude, Markers[i].Longitude);

            float dx = location.X - screen.X;
            float dy = location.Y - screen.Y + radius;

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

