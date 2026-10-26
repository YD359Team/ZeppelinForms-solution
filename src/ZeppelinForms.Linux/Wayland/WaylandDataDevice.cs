using System.Text;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.DragDrop;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>
/// The seat's wl_data_device: the clipboard, and the windows as drop targets.
/// </summary>
/// <remarks>
/// <para>
/// Wayland hands data between clients through pipes: the one that wants it creates a pipe
/// and sends its writing end with <c>receive</c>, the one that has it writes into the end
/// it gets in <c>send</c>. Every offer — the clipboard's, a drag's — comes first as a
/// wl_data_offer with its MIME types, before the event that says what it is for.
/// </para>
/// <para>
/// The compositor gives the clipboard only to the client with the keyboard: a window that
/// isn't focused reads nothing, and taking the clipboard needs the serial of a recent key
/// or button — <see cref="WaylandSeat.LastInputSerial"/>.
/// </para>
/// <para>
/// A drop brings files as text/uri-list and text as UTF-8, as the X11 platform takes the
/// former; the data itself is asked for only on the drop, so while dragging the form decides
/// by what is offered.
/// </para>
/// </remarks>
internal sealed class WaylandDataDevice : IClipboard, IDisposable
{
    private const string UriList = "text/uri-list";

    /// <summary>The text types, the best first: what GTK and Qt offer, and X11's old names
    /// that XWayland's clients offer.</summary>
    private static readonly string[] TextTypes =
        ["text/plain;charset=utf-8", "UTF8_STRING", "text/plain", "TEXT", "STRING"];

    /// <summary>How long the clipboard waits for another client, as on X11.</summary>
    private const int ReadTimeoutMs = 500;

    /// <summary>How long a drop waits for its data: files of a slow source come later.</summary>
    private const int DropTimeoutMs = 2000;

    private readonly WaylandPlatform _platform;
    private readonly WaylandSeat _seat;
    private readonly WlDataDeviceManager _manager;
    private readonly WlDataDevice _device;

    /// <summary>The MIME types of every offer the compositor announced, until it is destroyed.</summary>
    private readonly Dictionary<WlDataOffer, List<string>> _offerTypes = [];

    // the clipboard: what another client has on it, and what this one put there
    private WlDataOffer? _selection;
    private WlDataSource? _source;
    private string? _ownedText;

    // the drag over a window: its offer, the serial of its enter, where it is
    private WlDataOffer? _dragOffer;
    private WaylandWindow? _dragWindow;
    private uint _dragSerial;
    private Point _dragAt;
    private DragDropEffect _dragEffect;

    public WaylandDataDevice(WaylandPlatform platform, WaylandSeat seat, WlDataDeviceManager manager)
    {
        _platform = platform;
        _seat = seat;
        _manager = manager;
        _device = manager.GetDataDevice(seat.Proxy);

        _device.DataOffer += OnDataOffer;
        _device.Selection += OnSelection;
        _device.Enter += OnDragEnter;
        _device.Motion += OnDragMotion;
        _device.Leave += OnDragLeave;
        _device.Drop += OnDrop;
    }

    // ---- offers ----

    private void OnDataOffer(WlDataOffer offer)
    {
        var types = new List<string>();
        _offerTypes[offer] = types;

        // the types come right after the offer, before what it is for
        offer.Offer += types.Add;
    }

    private void Forget(WlDataOffer? offer)
    {
        if (offer is null) return;

        _offerTypes.Remove(offer);

        if (offer.IsAlive) offer.Destroy();
    }

    private IReadOnlyList<string> TypesOf(WlDataOffer offer) =>
        _offerTypes.TryGetValue(offer, out List<string>? types) ? types : [];

    private string? TextTypeOf(WlDataOffer offer)
    {
        IReadOnlyList<string> types = TypesOf(offer);

        foreach (string type in TextTypes)
            if (types.Contains(type))
                return type;

        return null;
    }

    /// <summary>Ask for the data of an offer in a type, and wait for it.</summary>
    private byte[]? Receive(WlDataOffer offer, string type, int timeoutMs)
    {
        if (UnixPipe.Create() is not var (read, write)) return null;

        offer.Receive(type, write);

        // our copy of the writing end goes: the reader sees the end when the other
        // client closes its copy
        UnixPipe.Close(write);

        // the request is on its way before the wait begins
        _platform.Flush();

        return UnixPipe.ReadToEnd(read, timeoutMs);
    }

    // ---- the clipboard ----

    private void OnSelection(WlDataOffer? offer)
    {
        // the offer the selection was is no use any more — unless a drag holds it
        if (!ReferenceEquals(_selection, _dragOffer)) Forget(_selection);

        _selection = offer;
    }

    public string? GetText()
    {
        // what this client put there is read without a round through the compositor —
        // and without a pipe to itself that nothing would read while it writes
        if (_source is not null) return _ownedText;

        if (_selection is not { } offer || TextTypeOf(offer) is not { } type)
            return null;

        return Receive(offer, type, ReadTimeoutMs) is { } bytes ? Decode(bytes, type) : null;
    }

    public void SetText(string text)
    {
        WlDataSource source = _manager.CreateDataSource();

        foreach (string type in TextTypes)
            source.Offer(type);

        source.Send += (_, fd) => UnixPipe.WriteAll(fd, Encoding.UTF8.GetBytes(_ownedText ?? string.Empty), 1000);

        // another client took the clipboard: this source is done
        source.Cancelled += () =>
        {
            if (ReferenceEquals(_source, source))
            {
                _source = null;
                _ownedText = null;
            }

            source.Destroy();
        };

        WlDataSource? previous = _source;

        _source = source;
        _ownedText = text;

        // the serial of the key or the click that asked for the copy: the compositor
        // refuses a selection set without input behind it
        _device.SetSelection(source, _seat.LastInputSerial);

        previous?.Destroy();
    }

    /// <summary>UTF-8 for the types that name it, and for the old ones whose bytes every
    /// application writes as UTF-8 today.</summary>
    private static string Decode(byte[] bytes, string type)
    {
        string text = type == "STRING" ? Encoding.Latin1.GetString(bytes) : Encoding.UTF8.GetString(bytes);

        // some applications end the text with the C string's zero
        return text.TrimEnd('\0');
    }

    // ---- the windows as drop targets ----

    private void OnDragEnter(uint serial, WlSurface? surface, double x, double y, WlDataOffer? offer)
    {
        _dragSerial = serial;
        _dragOffer = offer;
        _dragWindow = _platform.FindWindow(surface) is { AcceptsDrops: true } window ? window : null;
        _dragAt = new Point((float)x, (float)y);

        UpdateDrag();
    }

    private void OnDragMotion(uint time, double x, double y)
    {
        _dragAt = new Point((float)x, (float)y);
        UpdateDrag();
    }

    /// <summary>Ask the form what it would do with the drag here, and tell the source.</summary>
    private void UpdateDrag()
    {
        if (_dragOffer is not { } offer) return;

        _dragEffect = DragDropEffect.None;

        if (_dragWindow is { IsAlive: true, IsInputEnabled: true } window)
        {
            // the data comes only with the drop: the form decides by what is offered,
            // through stand-ins, as on X11
            IReadOnlyList<string> types = TypesOf(offer);

            var probe = new DragDropData
            {
                Files = types.Contains(UriList) ? [string.Empty] : [],
                Text = TextTypeOf(offer) is not null ? " " : null,
            };

            _dragEffect = window.Form.OnDragOverWindow(probe, window.ToFormPoint(_dragAt), _seat.Modifiers);
        }

        string? type = _dragEffect == DragDropEffect.None ? null : PreferredDropType(offer);

        if (type is null) _dragEffect = DragDropEffect.None;

        offer.Accept(_dragSerial, type);

        // version 3 negotiates the action: copy unless the form moves
        if (offer.Version >= 3)
        {
            uint copy = (uint)WlDataDeviceManager.DndAction.Copy;
            uint move = (uint)WlDataDeviceManager.DndAction.Move;

            if (_dragEffect == DragDropEffect.None)
                offer.SetActions(0, 0);
            else
                offer.SetActions(copy | move, _dragEffect == DragDropEffect.Move ? move : copy);
        }
    }

    /// <summary>Files before text: a file manager offers both, and the paths are what is meant.</summary>
    private string? PreferredDropType(WlDataOffer offer) =>
        TypesOf(offer).Contains(UriList) ? UriList : TextTypeOf(offer);

    private void OnDragLeave()
    {
        // after a drop the compositor still says the drag left: the drop has ended it
        if (_dragOffer is null) return;

        _dragWindow?.Form.OnDragLeaveWindow();

        EndDrag();
    }

    private void OnDrop()
    {
        if (_dragOffer is not { } offer) return;

        WaylandWindow? window = _dragWindow;
        string? type = PreferredDropType(offer);

        if (window is not { IsAlive: true } || _dragEffect == DragDropEffect.None || type is null)
        {
            window?.Form.OnDragLeaveWindow();
            EndDrag();
            return;
        }

        DragDropData data = type == UriList
            ? new DragDropData { Files = ParseUris(Receive(offer, type, DropTimeoutMs)) }
            : new DragDropData { Text = Receive(offer, type, DropTimeoutMs) is { } bytes ? Decode(bytes, type) : null };

        DragDropEffect effect = window.Form.OnDropWindow(data, window.ToFormPoint(_dragAt), _seat.Modifiers);

        // the source learns the drop is done — from version 3 it waits for this
        if (effect != DragDropEffect.None && offer.Version >= 3)
            offer.Finish();

        EndDrag();
    }

    private void EndDrag()
    {
        // the selection may be the very same offer: then it stays
        if (!ReferenceEquals(_dragOffer, _selection)) Forget(_dragOffer);

        _dragOffer = null;
        _dragWindow = null;
        _dragEffect = DragDropEffect.None;
    }

    /// <summary>The local paths of a text/uri-list: file:///home/user/%D1%84.txt and the like.</summary>
    private static string[] ParseUris(byte[]? bytes)
    {
        if (bytes is null) return [];

        List<string> files = [];

        foreach (string line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();

            // comments in the text/uri-list format start with a hash
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                files.Add(uri.LocalPath);
        }

        return [.. files];
    }

    /// <summary>The window closed: a drag over it ends there.</summary>
    public void Forget(WaylandWindow window)
    {
        if (ReferenceEquals(_dragWindow, window)) _dragWindow = null;
    }

    public void Dispose()
    {
        _source?.Destroy();
        _source = null;

        foreach (WlDataOffer offer in _offerTypes.Keys.ToList())
            Forget(offer);

        _selection = null;
        _dragOffer = null;

        if (!_device.IsAlive) return;

        // wl_data_device.release is version 2; an older one is only forgotten
        if (_device.Version >= 2) _device.Release();
        else _device.Abandon();
    }
}