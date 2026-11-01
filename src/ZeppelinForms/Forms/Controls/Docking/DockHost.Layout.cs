using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Docking;

/// <summary>The arrangement as JSON: saved, and read back into the same items.</summary>
/// <remarks>
/// <code>
/// {
///   "version": 1,
///   "root": { "split": "horizontal", "weight": 1, "children": [
///     { "group": ["files"], "selected": "files", "weight": 0.25 },
///     { "group": ["editor", "readme"], "selected": "editor", "weight": 0.75 } ] },
///   "floating": [ { "bounds": { "x": 40, "y": 30, "width": 320, "height": 240 },
///                   "group": ["search"], "selected": "search" } ],
///   "autoHidden": [ { "id": "output", "side": "bottom", "extent": 180 } ],
///   "hidden": [ "properties" ],
///   "active": "editor"
/// }
/// </code>
/// The writer and the reader are by hand, without reflection: the layout is read the same
/// after trimming and AOT.
/// </remarks>
public partial class DockHost
{
    private const int LayoutVersion = 1;

    private static readonly JsonWriterOptions LayoutWriterOptions = new() { Indented = true };

    /// <summary>The arrangement of the items as JSON: the splits and their shares, the
    /// groups and their tabs, the windows, the edges, the hidden items, the active one.
    /// The items themselves are named by <see cref="DockItem.Id"/>.</summary>
    public string SaveLayout()
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var json = new Utf8JsonWriter(buffer, LayoutWriterOptions))
        {
            json.WriteStartObject();
            json.WriteNumber("version", LayoutVersion);

            json.WritePropertyName("root");

            if (_root is null) json.WriteNullValue();
            else WriteNode(json, _root);

            json.WriteStartArray("floating");

            foreach (DockFloat window in _floats)
            {
                json.WriteStartObject();

                json.WriteStartObject("bounds");
                json.WriteNumber("x", Round(window.Bounds.X));
                json.WriteNumber("y", Round(window.Bounds.Y));
                json.WriteNumber("width", Round(window.Bounds.Width));
                json.WriteNumber("height", Round(window.Bounds.Height));
                json.WriteEndObject();

                WriteGroupItems(json, window.Group);
                json.WriteEndObject();
            }

            json.WriteEndArray();

            json.WriteStartArray("autoHidden");

            for (int s = 0; s < 4; s++)
            {
                foreach (DockItem item in _autoHidden[s])
                {
                    json.WriteStartObject();
                    json.WriteString("id", item.Id);
                    json.WriteString("side", SideName((DockSide)s));

                    if (item.AutoHideExtent > 0) json.WriteNumber("extent", Round(item.AutoHideExtent));

                    json.WriteEndObject();
                }
            }

            json.WriteEndArray();

            json.WriteStartArray("hidden");

            foreach (DockItem item in _items)
                if (item.State == DockState.Hidden) json.WriteStringValue(item.Id);

            json.WriteEndArray();

            if (_active is null) json.WriteNull("active");
            else json.WriteString("active", _active.Id);

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static double Round(float value) => Math.Round(value, 4);

    private static void WriteNode(Utf8JsonWriter json, DockNode node)
    {
        json.WriteStartObject();

        switch (node)
        {
            case DockSplit split:
                json.WriteString("split", split.Orientation == Orientation.Horizontal ? "horizontal" : "vertical");
                json.WriteNumber("weight", Round(split.Weight));
                json.WriteStartArray("children");

                foreach (DockNode child in split.Children) WriteNode(json, child);

                json.WriteEndArray();
                break;

            case DockGroup group:
                WriteGroupItems(json, group);
                json.WriteNumber("weight", Round(group.Weight));
                break;
        }

        json.WriteEndObject();
    }

    private static void WriteGroupItems(Utf8JsonWriter json, DockGroup group)
    {
        json.WriteStartArray("group");

        foreach (DockItem item in group.Items) json.WriteStringValue(item.Id);

        json.WriteEndArray();

        if (group.Selected is { } selected) json.WriteString("selected", selected.Id);
    }

    private static string SideName(DockSide side) => side switch
    {
        DockSide.Top => "top",
        DockSide.Right => "right",
        DockSide.Bottom => "bottom",
        _ => "left",
    };

    // ===== reading =====

    /// <summary>Arrange the items as a saved layout says. An item is the host's of the
    /// same id, or the one <paramref name="resolve"/> makes for it; an id neither knows is
    /// left out. The items of the host the layout doesn't name are hidden.</summary>
    /// <exception cref="FormatException">The text is not a layout. Nothing is changed then.</exception>
    public void LoadLayout(string json, Func<string, DockItem?>? resolve = null)
    {
        ArgumentNullException.ThrowIfNull(json);

        var reader = new LayoutReader(this, resolve);

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            reader.Read(document.RootElement);
        }
        catch (JsonException e)
        {
            throw new FormatException("The text is not a dock layout: " + e.Message, e);
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException && e is not ObjectDisposedException)
        {
            // an element of the wrong kind — a number where a string was to be — or
            // without a part it must have: a window's bounds without a width
            throw new FormatException("The text is not a dock layout: " + e.Message, e);
        }

        Apply(reader);
    }

    /// <summary>The layout read through, with its items resolved — before anything of the
    /// host is touched.</summary>
    private sealed class LayoutReader(DockHost host, Func<string, DockItem?>? resolve)
    {
        private readonly Dictionary<string, DockItem> _byId = new(StringComparer.Ordinal);
        private readonly HashSet<DockItem> _placed = new(ReferenceEqualityComparer.Instance);

        public DockNode? Root { get; private set; }

        public List<DockFloat> Floats { get; } = [];

        public List<(DockItem Item, DockSide Side, float Extent)> AutoHidden { get; } = [];

        public List<DockItem> Hidden { get; } = [];

        public DockItem? Active { get; private set; }

        public IEnumerable<DockItem> NewItems => _byId.Values.Where(item => item.Host is null);

        public void Read(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("The text is not a dock layout: an object is expected.");

            if (root.TryGetProperty("version", out JsonElement version) && version.GetInt32() > LayoutVersion)
                throw new FormatException($"The layout is of version {version.GetInt32()}; this host reads {LayoutVersion}.");

            if (root.TryGetProperty("root", out JsonElement node) && node.ValueKind != JsonValueKind.Null)
                Root = DockTree.Normalize(ReadNode(node));

            if (Root is not null) Root.Parent = null;

            if (root.TryGetProperty("floating", out JsonElement floating))
            {
                foreach (JsonElement window in floating.EnumerateArray())
                {
                    DockGroup group = ReadGroup(window);

                    if (group.Items.Count == 0) continue;

                    Rectangle bounds = new(40, 40, 320, 240);

                    if (window.TryGetProperty("bounds", out JsonElement b))
                    {
                        bounds = new Rectangle(
                            b.GetProperty("x").GetSingle(),
                            b.GetProperty("y").GetSingle(),
                            b.GetProperty("width").GetSingle(),
                            b.GetProperty("height").GetSingle());
                    }

                    var place = new DockFloat(group, bounds);
                    group.Float = place;
                    Floats.Add(place);
                }
            }

            if (root.TryGetProperty("autoHidden", out JsonElement autoHidden))
            {
                foreach (JsonElement entry in autoHidden.EnumerateArray())
                {
                    if (!entry.TryGetProperty("id", out JsonElement id) || Take(id.GetString()) is not { } item) continue;

                    DockSide side = entry.TryGetProperty("side", out JsonElement s) ? ParseSide(s.GetString()) : DockSide.Left;
                    float extent = entry.TryGetProperty("extent", out JsonElement e) ? e.GetSingle() : 0f;

                    AutoHidden.Add((item, side, extent));
                }
            }

            if (root.TryGetProperty("hidden", out JsonElement hidden))
            {
                foreach (JsonElement id in hidden.EnumerateArray())
                    if (Take(id.GetString()) is { } item) Hidden.Add(item);
            }

            if (root.TryGetProperty("active", out JsonElement active) && active.ValueKind == JsonValueKind.String)
                Active = Find(active.GetString());
        }

        private DockNode? ReadNode(JsonElement node)
        {
            if (node.ValueKind != JsonValueKind.Object)
                throw new FormatException("The text is not a dock layout: a node is to be an object.");

            float weight = node.TryGetProperty("weight", out JsonElement w) ? Math.Max(0.0001f, w.GetSingle()) : 1f;

            if (node.TryGetProperty("split", out JsonElement kind))
            {
                Orientation orientation = kind.GetString() switch
                {
                    "horizontal" => Orientation.Horizontal,
                    "vertical" => Orientation.Vertical,
                    var other => throw new FormatException($"The text is not a dock layout: a split '{other}'."),
                };

                var split = new DockSplit(orientation) { Weight = weight };

                if (node.TryGetProperty("children", out JsonElement children))
                {
                    foreach (JsonElement child in children.EnumerateArray())
                    {
                        if (ReadNode(child) is not { } read) continue;

                        read.Parent = split;
                        split.Children.Add(read);
                    }
                }

                return split;
            }

            if (node.TryGetProperty("group", out _))
            {
                DockGroup group = ReadGroup(node);
                group.Weight = weight;

                return group;
            }

            throw new FormatException("The text is not a dock layout: a node is a split or a group.");
        }

        private DockGroup ReadGroup(JsonElement node)
        {
            var group = new DockGroup();

            if (node.TryGetProperty("group", out JsonElement ids))
            {
                foreach (JsonElement id in ids.EnumerateArray())
                    if (Take(id.GetString()) is { } item) group.Items.Add(item);
            }

            if (node.TryGetProperty("selected", out JsonElement selected) && selected.ValueKind == JsonValueKind.String)
                group.Selected = group.Items.FirstOrDefault(item => item.Id == selected.GetString());

            group.Selected ??= group.Items.FirstOrDefault();

            return group;
        }

        /// <summary>The item of an id, once: an id met twice is placed where it came first.</summary>
        private DockItem? Take(string? id)
        {
            if (Find(id) is not { } item || !_placed.Add(item)) return null;

            return item;
        }

        private DockItem? Find(string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (_byId.TryGetValue(id, out DockItem? known)) return known;

            DockItem? item = host.FindItem(id) ?? resolve?.Invoke(id);

            if (item is null) return null;

            if (item.Id != id)
                throw new ArgumentException($"The item made for '{id}' is '{item.Id}'.");

            if (item.Host is not null && item.Host != host)
                throw new ArgumentException($"The item '{id}' is in another host.");

            _byId[id] = item;
            return item;
        }

        private static DockSide ParseSide(string? side) => side switch
        {
            "top" => DockSide.Top,
            "right" => DockSide.Right,
            "bottom" => DockSide.Bottom,
            _ => DockSide.Left,
        };
    }

    /// <summary>The layout read takes the place of the arrangement.</summary>
    private void Apply(LayoutReader layout)
    {
        CancelDrag();
        CloseAutoHide(sync: false);

        foreach (DockItem item in layout.NewItems)
        {
            item.Host = this;
            _items.Add(item);
        }

        // everything is taken down first: the layout says where each item is
        _root = null;
        _floats.Clear();
        _lastActiveGroup = null;

        foreach (List<DockItem> edge in _autoHidden) edge.Clear();

        foreach (DockItem item in _items)
        {
            item.Group = null;
            item.State = DockState.Hidden;
        }

        _root = layout.Root;

        foreach (DockGroup group in DockTree.Groups(_root))
            foreach (DockItem item in group.Items)
            {
                item.Group = group;
                item.State = DockState.Docked;
            }

        foreach (DockFloat window in layout.Floats)
        {
            _floats.Add(window);

            foreach (DockItem item in window.Group.Items)
            {
                item.Group = window.Group;
                item.State = DockState.Floating;
            }
        }

        foreach ((DockItem item, DockSide side, float extent) in layout.AutoHidden)
        {
            if (extent > 0) item.AutoHideExtent = extent;

            PlaceAutoHidden(item, side);
        }

        DockItem? previous = _active;

        _active = layout.Active is { State: DockState.Docked or DockState.Floating } chosen
            ? chosen
            : PrimaryGroup()?.Selected ?? _floats.LastOrDefault()?.Group.Selected;

        if (_active?.Group is { Float: null } activeGroup) _lastActiveGroup = activeGroup;

        Changed();

        if (!ReferenceEquals(previous, _active)) ActiveItemChanged?.Invoke(this, EventArgs.Empty);
    }
}
