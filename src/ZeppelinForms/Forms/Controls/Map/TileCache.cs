using ZeppelinForms.Drawing.Imaging;

namespace ZeppelinForms.Forms.Controls.Map;

/// <summary>
/// Кэш тайлов с вытеснением наименее востребованных и защитой от
/// повторных загрузок одного и того же тайла.
/// </summary>
internal sealed class TileCache
{
    private readonly record struct TileKey(int Zoom, int X, int Y);

    private readonly Dictionary<TileKey, Image> _tiles = [];
    private readonly LinkedList<TileKey> _usage = [];
    private readonly Dictionary<TileKey, LinkedListNode<TileKey>> _nodes = [];
    private readonly HashSet<TileKey> _loading = [];
    private readonly Lock _sync = new();

    public int Capacity { get; init; } = 400;

    public bool TryGet(int zoom, int x, int y, out Image? tile)
    {
        var key = new TileKey(zoom, x, y);

        lock (_sync)
        {
            if (!_tiles.TryGetValue(key, out tile))
                return false;

            // обращение поднимает тайл в начало списка: при вытеснении
            // уйдут те, которые давно не рисовались
            if (_nodes.TryGetValue(key, out LinkedListNode<TileKey>? node))
            {
                _usage.Remove(node);
                _usage.AddFirst(node);
            }

            return true;
        }
    }

    /// <summary>Отметить тайл как загружаемый. false — загрузка уже идёт.</summary>
    public bool TryBeginLoad(int zoom, int x, int y)
    {
        lock (_sync)
            return _loading.Add(new TileKey(zoom, x, y));
    }

    public void EndLoad(int zoom, int x, int y)
    {
        lock (_sync)
            _loading.Remove(new TileKey(zoom, x, y));
    }

    public void Put(int zoom, int x, int y, Image tile)
    {
        var key = new TileKey(zoom, x, y);

        lock (_sync)
        {
            if (_tiles.ContainsKey(key)) return;

            _tiles[key] = tile;
            _nodes[key] = _usage.AddFirst(key);

            while (_usage.Count > Capacity)
            {
                LinkedListNode<TileKey>? last = _usage.Last;
                if (last is null) break;

                _usage.RemoveLast();
                _nodes.Remove(last.Value);
                _tiles.Remove(last.Value);
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _tiles.Clear();
            _usage.Clear();
            _nodes.Clear();
        }
    }
}